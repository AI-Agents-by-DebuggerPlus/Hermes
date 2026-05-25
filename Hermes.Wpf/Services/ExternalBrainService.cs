using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Cached Markdown memories from Obsidian-style vault folders.</summary>
public sealed class ExternalBrainService : IDisposable
{
    private static readonly Regex TokenSplit = new(@"[\s,.;:!?()[\]{}""'`]+", RegexOptions.Compiled);

    private readonly LogService _log;
    private readonly HermesSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly MemoryVectorIndex _vectorIndex;
    private RoleAwareMemoryRouter? _roleRouter;

    private readonly object _cacheLock = new();
    private ImmutableList<MemoryItem> _memories = ImmutableList<MemoryItem>.Empty;

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadDebounce;
    private volatile bool _busyLoad;
    private bool _disposed;

    public ExternalBrainService(LogService log, HermesSettings settings, Dispatcher dispatcher)
    {
        _log = log;
        _settings = settings;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _vectorIndex = new MemoryVectorIndex(log);
        EnsureDebounceTimer();
        var vault = ResolveEffectiveMemoryPath();
        EnglishLearningVaultPaths.EnsureLayout(vault);
        VaultInitializer.EnsureLayout(vault);
        RestartWatcherUnsafe();
        _ = ReloadFromDiskAsync("service-ctor");
    }

    public event Action? MemoriesChanged;

    public void SetRoleRouter(RoleAwareMemoryRouter router) => _roleRouter = router;

    public string ResolveEffectiveMemoryPath()
    {
        try
        {
            var env = Environment.GetEnvironmentVariable("HERMES_EXTERNAL_BRAIN_PATH")?.Trim();
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            {
                return Path.GetFullPath(env);
            }

            var appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HermesWpf");
            var overlay = Path.Combine(appRoot, "externalBrain.json");
            if (File.Exists(overlay))
            {
                try
                {
                    var txt = File.ReadAllText(overlay);
                    var o = JsonSerializer.Deserialize<ExternalBrainFileConfig>(txt);
                    var p = o?.MemoryPath?.Trim();
                    if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                    {
                        return Path.GetFullPath(p);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarn($"[external-brain] externalBrain.json: {ex.Message}");
                }
            }

            var s = (_settings.ExternalBrainMemoryPath ?? string.Empty).Trim();
            return string.IsNullOrEmpty(s)
                ? string.Empty
                : Path.GetFullPath(s);
        }
        catch
        {
            return string.Empty;
        }
    }

    public Task<List<MemoryItem>> GetAllMemoriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SnapshotOrdered());
    }

    public async Task<List<MemoryItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q))
        {
            return SnapshotOrdered();
        }

        if (_settings.ExternalBrainVectorRetrievalEnabled)
        {
            var vectorHits = await _vectorIndex.SelectTopAsync(q, 50, _settings, cancellationToken)
                .ConfigureAwait(false);
            if (vectorHits.Count > 0)
            {
                return vectorHits;
            }
        }

        var tokens = Tokenize(q);
        var scored = MemoryLexicalScorer.Score(tokens, Snapshot());
        return scored
            .OrderByDescending(kv => kv.Score)
            .ThenByDescending(kv => kv.M.Timestamp)
            .ThenByDescending(kv => kv.M.Importance)
            .Select(kv => kv.M)
            .ToList();
    }

    public Task<List<MemoryItem>> GetRecentAsync(TimeSpan timeSpan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cutoff = DateTime.UtcNow - timeSpan;
        return Task.FromResult(Snapshot()
            .Where(m => m.Timestamp.ToUniversalTime() >= cutoff)
            .OrderByDescending(m => m.Timestamp)
            .ToList());
    }

    public Task<List<MemoryItem>> GetByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var t = (tag ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant();
        if (string.IsNullOrEmpty(t))
        {
            return Task.FromResult(new List<MemoryItem>());
        }

        return Task.FromResult(Snapshot()
            .Where(m => m.Tags.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(m => m.Timestamp)
            .ToList());
    }

    public async Task<string> BuildContextAsync(string userQuery, int maxItems) =>
        (await BuildContextDetailedAsync(userQuery, maxItems).ConfigureAwait(false)).Block;

    public async Task<(string Block, IReadOnlyList<MemoryItem> Items)> BuildContextDetailedAsync(
        string userQuery,
        int maxItems)
    {
        var path = ResolveEffectiveMemoryPath();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return (string.Empty, []);
        }

        var cap = Math.Clamp(maxItems, 1, 20);
        var snap = Snapshot();
        List<MemoryItem> candidates;
        var poolSize = _roleRouter is not null && _roleRouter.CurrentRole != AgentRole.Universal
            ? Math.Min(cap * 2, 20)
            : cap;

        if (_settings.ExternalBrainVectorRetrievalEnabled)
        {
            candidates = await _vectorIndex.SelectTopAsync(userQuery ?? string.Empty, poolSize, _settings)
                .ConfigureAwait(false);
        }
        else
        {
            var tokens = Tokenize(userQuery ?? string.Empty);
            candidates = MemoryLexicalScorer.Score(tokens, snap)
                .OrderByDescending(kv => kv.Score)
                .ThenByDescending(kv => kv.M.Timestamp)
                .ThenByDescending(kv => kv.M.Importance)
                .Take(poolSize)
                .Select(kv => kv.M)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            candidates = snap.OrderByDescending(m => m.Timestamp).Take(poolSize).ToList();
        }

        if (_roleRouter is not null)
        {
            candidates = _roleRouter.FilterAndBoost(candidates, userQuery ?? string.Empty, cap).ToList();
        }
        else if (candidates.Count > cap)
        {
            candidates = candidates.Take(cap).ToList();
        }

        if (candidates.Count == 0)
        {
            return (string.Empty, []);
        }

        return (FormatContextBlock(candidates), candidates);
    }

    private static string FormatContextBlock(IReadOnlyList<MemoryItem> candidates)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("--- EXTERNAL BRAIN (Markdown vault excerpts) ---");
        foreach (var m in candidates)
        {
            sb.Append('[')
                .Append(m.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                .Append("] ");
            if (!string.IsNullOrWhiteSpace(m.Type))
            {
                sb.Append(m.Type).Append(" · ");
            }

            sb.Append("imp=").Append(m.Importance).Append(" · ")
                .Append(Path.GetFileName(m.SourceFile))
                .AppendLine();
            if (m.Tags.Count > 0)
            {
                sb.AppendLine(string.Join(", ", m.Tags.Select(static t => '#' + t)));
            }

            if (!string.IsNullOrWhiteSpace(m.Project))
            {
                sb.Append("project: ").AppendLine(m.Project);
            }

            sb.AppendLine(m.Content.Trim());
            sb.AppendLine("---");
        }

        sb.AppendLine("Treat as factual user memory unless contradicted by the user.");
        sb.AppendLine("--- END EXTERNAL BRAIN ---");
        return sb.ToString();
    }

    /// <summary>Sync facade (uses in-memory cache only).</summary>
    public string BuildContext(string userQuery, int maxItems = 10) =>
        BuildContextAsync(userQuery, maxItems).GetAwaiter().GetResult();

    public void RestartWatcherAndReload(string reasonTag)
    {
        if (_disposed)
        {
            return;
        }

        RestartWatcherUnsafe();
        _ = ReloadFromDiskAsync(reasonTag);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopWatcherUnsafe();
        _reloadDebounce?.Stop();
        _reloadDebounce = null;
        GC.SuppressFinalize(this);
    }

    private void EnsureDebounceTimer()
    {
        if (_reloadDebounce is not null)
        {
            return;
        }

        _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce!.Stop();
            _ = ReloadFromDiskAsync(nameof(FileSystemWatcher));
        };
    }

    private void ScheduleDebouncedReload(string reasonTag)
    {
        if (_disposed)
        {
            return;
        }

        _log.LogInfo($"[external-brain] change observed → debounce reload ({reasonTag})");
        _dispatcher.BeginInvoke(() =>
        {
            EnsureDebounceTimer();
            _reloadDebounce!.Stop();
            _reloadDebounce.Start();
        });
    }

    private void RestartWatcherUnsafe()
    {
        StopWatcherUnsafe();
        var path = ResolveEffectiveMemoryPath();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter =
                    NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                Filter = "*.md",
            };
            _watcher.Changed += (_, e) => ScheduleDebouncedReload($"Changed:{e.Name}");
            _watcher.Created += (_, e) => ScheduleDebouncedReload($"Created:{e.Name}");
            _watcher.Deleted += (_, e) => ScheduleDebouncedReload($"Deleted:{e.Name}");
            _watcher.Renamed += (_, _) => ScheduleDebouncedReload("Renamed");
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[external-brain] FileSystemWatcher failed: {ex.Message}");
        }
    }

    private void StopWatcherUnsafe()
    {
        if (_watcher is null)
        {
            return;
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        catch
        {
            // ignore
        }

        _watcher = null;
    }

    private ImmutableList<MemoryItem> Snapshot()
    {
        lock (_cacheLock)
        {
            return _memories;
        }
    }

    private List<MemoryItem> SnapshotOrdered() =>
        Snapshot().OrderByDescending(m => m.Timestamp).ToList();

    private async Task ReloadFromDiskAsync(string reasonTag)
    {
        if (_disposed || _busyLoad)
        {
            return;
        }

        _busyLoad = true;
        try
        {
            await Task.Run(() =>
            {
                var path = ResolveEffectiveMemoryPath();
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    lock (_cacheLock)
                    {
                        _memories = ImmutableList<MemoryItem>.Empty;
                    }

                    return;
                }

                EnglishLearningVaultPaths.EnsureLayout(path);

                var list = new List<MemoryItem>();
                foreach (var file in Directory.EnumerateFiles(path, "*.md", SearchOption.AllDirectories))
                {
                    var item = TryParseMarkdownFile(file);
                    if (item is not null)
                    {
                        list.Add(item);
                    }
                }

                var ordered = list
                    .OrderByDescending(m => m.Timestamp)
                    .ToImmutableList();

                lock (_cacheLock)
                {
                    _memories = ordered;
                }

                if (ordered.IsEmpty && Directory.Exists(path))
                {
                    _log.LogWarn("[external-brain] USER ACTION REQUIRED: No memory files found");
                }

                _log.LogInfo($"[external-brain] loaded {ordered.Count} *.md from {path} ({reasonTag})");
            }).ConfigureAwait(false);

            try
            {
                var vaultPath = ResolveEffectiveMemoryPath();
                var snap = Snapshot();
                await _vectorIndex.RebuildAsync(snap, _settings, vaultPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarn($"[vector-memory] index rebuild: {ex.Message}");
            }

            NotifyMemoriesChanged();
        }
        finally
        {
            _busyLoad = false;
        }
    }

    private MemoryItem? TryParseMarkdownFile(string fullPath)
    {
        try
        {
            var raw = File.ReadAllText(fullPath);
            var info = new FileInfo(fullPath);
            DateTime timestampLocal;
            var type = string.Empty;
            var project = string.Empty;
            var importance = 3;
            BrainYamlFrontmatter yaml = default;
            if (ExternalBrainMarkdown.TrySplitYamlFrontmatter(raw, out var yamlRaw, out _))
            {
                yaml = ExternalBrainMarkdown.ParseYamlBlock(yamlRaw);
                if (!string.IsNullOrWhiteSpace(yaml.Type))
                {
                    type = yaml.Type.Trim().ToLowerInvariant();
                }

                project = yaml.Project ?? string.Empty;
                if (yaml.Importance is { } yi)
                {
                    importance = yi;
                }

                timestampLocal = yaml.TimestampLocal
                    ?? (ExternalBrainMarkdown.TryGetFilenameTimestamp(fullPath, out var fromName)
                        ? fromName
                        : info.LastWriteTimeUtc.ToLocalTime());
            }
            else if (ExternalBrainMarkdown.TryGetFilenameTimestamp(fullPath, out var fromName2))
            {
                timestampLocal = fromName2;
            }
            else
            {
                timestampLocal = info.LastWriteTimeUtc.ToLocalTime();
            }

            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in yaml.YamlTags ?? [])
            {
                tags.Add(t);
            }

            foreach (var t in ExternalBrainMarkdown.ExtractHashtagTags(raw))
            {
                tags.Add(t);
            }

            var body = ExternalBrainMarkdown.CleanContentBody(raw);
            return new MemoryItem
            {
                Type = type,
                Timestamp = timestampLocal,
                Tags = tags.OrderBy(static t => t, StringComparer.Ordinal).ToList(),
                Project = project,
                Importance = importance,
                Content = body,
                RawMarkdown = raw,
                SourceFile = fullPath,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[external-brain] skip {fullPath}: {ex.Message}");
            return null;
        }
    }

    private void NotifyMemoriesChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            MemoriesChanged?.Invoke();
        }
        else
        {
            _dispatcher.BeginInvoke((Action)(() => MemoriesChanged?.Invoke()));
        }
    }

    private static ImmutableList<string> Tokenize(string query)
    {
        return TokenSplit
            .Split(query.ToLowerInvariant())
            .Select(t => t.Trim())
            .Where(t => t.Length > 1)
            .Distinct()
            .ToImmutableList();
    }

    private static double RankMetaOnly(MemoryItem m)
    {
        var imp = Math.Max(1, m.Importance);
        return 12.0 * imp * RecencyMultiplier(m);
    }

    /// <summary>1.0…~1.28 — boosts recent memories in context ranking.</summary>
    private static double RecencyMultiplier(MemoryItem m)
    {
        var hours = (DateTime.UtcNow - m.Timestamp.ToUniversalTime()).TotalHours;
        if (hours < 24)
        {
            return 1.28;
        }

        if (hours < 24 * 7)
        {
            return 1.1;
        }

        if (hours < 24 * 30)
        {
            return 1.03;
        }

        return 1.0;
    }
}
