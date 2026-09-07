using System.IO;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Fetches tradeable MT5 symbols via Mt5Terminal IPC and caches them for Trading Analytics.
/// </summary>
public sealed class Mt5TerminalSymbolsService
{
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromDays(7);
    private readonly Mt5TerminalIpcClient _ipc;
    private readonly LogService _log;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    public Mt5TerminalSymbolsService(Mt5TerminalIpcClient ipc, LogService log)
    {
        _ipc = ipc;
        _log = log;
    }

    public static string ResolveCachePath(string? tradingAnalyticsProjectPath) =>
        Path.Combine(
            HermesProjectLayout.GetHermesDirectory(tradingAnalyticsProjectPath ?? string.Empty),
            "mt5_symbols.json");

    public static string ResolveIpcSymbolsPath(string? mt5ProjectPath) =>
        Path.Combine(Mt5TerminalIpcClient.ResolveIpcDir(mt5ProjectPath), "symbols.json");

    public bool ShouldRefreshCache(string? tradingAnalyticsProjectPath, TimeSpan? ttl = null)
    {
        var path = ResolveCachePath(tradingAnalyticsProjectPath);
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            return age > (ttl ?? DefaultCacheTtl);
        }
        catch
        {
            return true;
        }
    }

    public Mt5SymbolsCatalog? TryLoadCache(string? tradingAnalyticsProjectPath)
    {
        var path = ResolveCachePath(tradingAnalyticsProjectPath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return ParseCatalog(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[mt5-symbols] cache read failed: {ex.Message}");
            return null;
        }
    }

    public async Task<Mt5SymbolsCatalog?> EnsureCachedAsync(
        string tradingAnalyticsProjectPath,
        string? mt5ProjectPath,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && !ShouldRefreshCache(tradingAnalyticsProjectPath))
        {
            var cached = TryLoadCache(tradingAnalyticsProjectPath);
            if (cached is not null)
            {
                return cached;
            }
        }

        return await FetchAndCacheAsync(tradingAnalyticsProjectPath, mt5ProjectPath, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Mt5SymbolsCatalog?> FetchAndCacheAsync(
        string tradingAnalyticsProjectPath,
        string? mt5ProjectPath,
        CancellationToken cancellationToken = default)
    {
        await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ShouldRefreshCache(tradingAnalyticsProjectPath))
            {
                var existing = TryLoadCache(tradingAnalyticsProjectPath);
                if (existing is not null)
                {
                    return existing;
                }
            }

            var cmd = new Mt5TerminalRouteCommand
            {
                Action = "list_symbols",
                Id = Guid.NewGuid().ToString("N"),
            };

            var exec = await _ipc
                .ExecuteAsync(cmd, mt5ProjectPath, TimeSpan.FromSeconds(45), cancellationToken)
                .ConfigureAwait(false);
            if (!exec.Ok)
            {
                _log.LogWarn($"[mt5-symbols] IPC failed: {exec.Error ?? exec.Message ?? "unknown"}");
                return null;
            }

            var ipcPath = exec.SymbolsPath ?? ResolveIpcSymbolsPath(mt5ProjectPath);
            if (!File.Exists(ipcPath))
            {
                _log.LogWarn($"[mt5-symbols] symbols file missing: {ipcPath}");
                return null;
            }

            var catalog = ParseCatalog(File.ReadAllText(ipcPath));
            if (catalog is null || catalog.Count == 0)
            {
                _log.LogWarn("[mt5-symbols] empty symbol list from Mt5Terminal");
                return null;
            }

            await SaveCacheAsync(tradingAnalyticsProjectPath, catalog, cancellationToken)
                .ConfigureAwait(false);
            _log.LogInfo($"[mt5-symbols] cached {catalog.Count} symbols → {ResolveCachePath(tradingAnalyticsProjectPath)}");
            return catalog;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    public static async Task SaveCacheAsync(
        string tradingAnalyticsProjectPath,
        Mt5SymbolsCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveCachePath(tradingAnalyticsProjectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dto = new CacheDto
        {
            FetchedAtUtc = catalog.FetchedAtUtc.ToString("o"),
            Source = catalog.Source,
            ChartSymbol = catalog.ChartSymbol,
            Symbols = catalog.Symbols.ToList(),
            Count = catalog.Count,
        };
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tmp, path);
    }

    public static Mt5SymbolsCatalog? ParseCatalog(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var symbols = new List<string>();
            if (root.TryGetProperty("symbols", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(s))
                        {
                            symbols.Add(s);
                        }
                    }
                }
            }

            if (symbols.Count == 0)
            {
                return null;
            }

            var fetchedAt = DateTime.UtcNow;
            if (root.TryGetProperty("fetched_at_utc", out var fa)
                && fa.ValueKind == JsonValueKind.String
                && DateTime.TryParse(fa.GetString(), out var parsed))
            {
                fetchedAt = parsed.ToUniversalTime();
            }
            else if (root.TryGetProperty("utc", out var utc)
                     && utc.ValueKind == JsonValueKind.String
                     && DateTime.TryParse(utc.GetString(), out var parsedUtc))
            {
                fetchedAt = parsedUtc.ToUniversalTime();
            }

            string? chart = null;
            if (root.TryGetProperty("chart_symbol", out var cs) && cs.ValueKind == JsonValueKind.String)
            {
                chart = cs.GetString();
            }

            var source = root.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String
                ? src.GetString() ?? "Mt5Terminal"
                : "Mt5Terminal";

            return new Mt5SymbolsCatalog
            {
                FetchedAtUtc = fetchedAt,
                Source = source,
                ChartSymbol = chart,
                Symbols = symbols,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class CacheDto
    {
        public string FetchedAtUtc { get; set; } = string.Empty;
        public string Source { get; set; } = "Mt5Terminal";
        public string? ChartSymbol { get; set; }
        public List<string> Symbols { get; set; } = [];
        public int Count { get; set; }
    }
}
