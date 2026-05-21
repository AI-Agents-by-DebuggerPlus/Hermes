using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>TF-IDF index with optional Ollama dense embeddings for External Brain retrieval.</summary>
public sealed class MemoryVectorIndex
{
    private static readonly Regex TokenSplit = new(@"[\s,.;:!?()[\]{}""'`]+", RegexOptions.Compiled);

    private readonly LogService _log;
    private readonly object _lock = new();

    private ImmutableList<MemoryItem> _items = ImmutableList<MemoryItem>.Empty;
    private Dictionary<string, float[]> _denseBySource = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _hashBySource = new(StringComparer.OrdinalIgnoreCase);
    private string[] _vocabulary = [];
    private float[] _idf = [];
    private Dictionary<string, float[]> _tfidfBySource = new(StringComparer.OrdinalIgnoreCase);
    private bool _useDense;
    private string _mode = "none";
    private volatile bool _rebuilding;

    public MemoryVectorIndex(LogService log) => _log = log;

    public string CurrentMode
    {
        get
        {
            lock (_lock)
            {
                return _mode;
            }
        }
    }

    public async Task RebuildAsync(
        IReadOnlyList<MemoryItem> items,
        HermesSettings settings,
        string vaultPath,
        CancellationToken cancellationToken = default)
    {
        if (_rebuilding)
        {
            return;
        }

        _rebuilding = true;
        try
        {
            var list = items.OrderByDescending(m => m.Timestamp).ToImmutableList();
            lock (_lock)
            {
                _items = list;
            }

            if (list.Count == 0)
            {
                lock (_lock)
                {
                    _denseBySource = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
                    _tfidfBySource = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
                    _vocabulary = [];
                    _idf = [];
                    _useDense = false;
                    _mode = "empty";
                }

                return;
            }

            BuildTfidfIndex(list);

            if (!settings.ExternalBrainVectorRetrievalEnabled || !settings.ExternalBrainUseOllamaEmbeddings)
            {
                lock (_lock)
                {
                    _useDense = false;
                    _mode = "tfidf";
                }

                _log.LogInfo($"[vector-memory] TF-IDF index ready ({list.Count} memories)");
                return;
            }

            var cachePath = ResolveCachePath(vaultPath);
            var cache = LoadCache(cachePath);
            var dense = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var model = NormalizeModel(settings.ExternalBrainEmbeddingModel);
            var updated = 0;
            var reused = 0;

            using var client = new OllamaEmbeddingClient(settings.ExternalBrainOllamaBaseUrl, _log);
            foreach (var item in list)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = MemoryEmbeddingText.ForMemory(item);
                var hash = MemoryEmbeddingText.ContentHash(item);
                hashes[item.SourceFile] = hash;

                if (cache.TryGetValue(item.SourceFile, out var cached)
                    && string.Equals(cached.Model, model, StringComparison.Ordinal)
                    && string.Equals(cached.ContentHash, hash, StringComparison.Ordinal)
                    && cached.Vector.Length > 0)
                {
                    dense[item.SourceFile] = cached.Vector;
                    reused++;
                    continue;
                }

                var vector = await client.TryEmbedAsync(model, text, cancellationToken).ConfigureAwait(false);
                if (vector is null || vector.Length == 0)
                {
                    lock (_lock)
                    {
                        _useDense = false;
                        _mode = "tfidf";
                        _denseBySource = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
                        _hashBySource = hashes;
                    }

                    _log.LogWarn("[vector-memory] Ollama embeddings unavailable — using TF-IDF fallback");
                    return;
                }

                dense[item.SourceFile] = Normalize(vector);
                cache[item.SourceFile] = new CacheEntry(model, hash, dense[item.SourceFile]);
                updated++;
            }

            SaveCache(cachePath, cache);
            lock (_lock)
            {
                _denseBySource = dense;
                _hashBySource = hashes;
                _useDense = true;
                _mode = "ollama";
            }

            _log.LogInfo(
                $"[vector-memory] Ollama index ready ({list.Count} memories, embedded={updated}, cached={reused}, model={model})");
        }
        finally
        {
            _rebuilding = false;
        }
    }

    public async Task<List<MemoryItem>> SelectTopAsync(
        string query,
        int maxItems,
        HermesSettings settings,
        CancellationToken cancellationToken = default)
    {
        var cap = Math.Clamp(maxItems, 1, 20);
        ImmutableList<MemoryItem> items;
        bool useDense;
        Dictionary<string, float[]> dense;
        Dictionary<string, float[]> tfidf;
        string[] vocabulary;
        float[] idf;

        lock (_lock)
        {
            items = _items;
            useDense = _useDense;
            dense = _denseBySource;
            tfidf = _tfidfBySource;
            vocabulary = _vocabulary;
            idf = _idf;
        }

        if (items.Count == 0)
        {
            return [];
        }

        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return items.Take(cap).ToList();
        }

        _log.LogInfo("[vector-memory] Querying vector memory…");

        float[]? queryVector = null;
        if (useDense && settings.ExternalBrainUseOllamaEmbeddings)
        {
            using var client = new OllamaEmbeddingClient(settings.ExternalBrainOllamaBaseUrl, _log);
            queryVector = await client.TryEmbedAsync(settings.ExternalBrainEmbeddingModel, q, cancellationToken)
                .ConfigureAwait(false);
            if (queryVector is not null)
            {
                queryVector = Normalize(queryVector);
            }
        }

        var ranked = new List<(MemoryItem M, double Score)>(items.Count);
        foreach (var item in items)
        {
            double sim;
            if (queryVector is not null && dense.TryGetValue(item.SourceFile, out var docDense))
            {
                sim = CosineSimilarity(queryVector, docDense);
            }
            else if (tfidf.TryGetValue(item.SourceFile, out var docTfidf))
            {
                var qTfidf = BuildTfidfVector(Tokenize(q), vocabulary, idf);
                sim = CosineSimilarity(qTfidf, docTfidf);
            }
            else
            {
                sim = 0;
            }

            var boosted = sim * MetaMultiplier(item);
            ranked.Add((item, boosted));
        }

        var top = ranked
            .OrderByDescending(kv => kv.Score)
            .ThenByDescending(kv => kv.M.Timestamp)
            .ThenByDescending(kv => kv.M.Importance)
            .Take(cap)
            .Select(kv => kv.M)
            .ToList();

        if (top.Count == 0)
        {
            top = items.Take(cap).ToList();
        }

        return top;
    }

    internal static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private void BuildTfidfIndex(IReadOnlyList<MemoryItem> items)
    {
        var docs = items
            .Select(m => (m, tokens: Tokenize(MemoryEmbeddingText.ForMemory(m))))
            .ToList();

        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, tokens) in docs)
        {
            foreach (var t in tokens.Distinct(StringComparer.Ordinal))
            {
                df.TryGetValue(t, out var c);
                df[t] = c + 1;
            }
        }

        var vocab = df
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(8192)
            .Select(kv => kv.Key)
            .ToArray();

        var n = Math.Max(1, docs.Count);
        var idf = new float[vocab.Length];
        for (var i = 0; i < vocab.Length; i++)
        {
            var documentFrequency = df[vocab[i]];
            idf[i] = (float)Math.Log((n + 1.0) / (documentFrequency + 1.0)) + 1f;
        }

        var tfidfBySource = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, tokens) in docs)
        {
            tfidfBySource[item.SourceFile] = BuildTfidfVector(tokens, vocab, idf);
        }

        lock (_lock)
        {
            _vocabulary = vocab;
            _idf = idf;
            _tfidfBySource = tfidfBySource;
        }
    }

    private static ImmutableList<string> Tokenize(string text) =>
        TokenSplit
            .Split((text ?? string.Empty).ToLowerInvariant())
            .Select(t => t.Trim())
            .Where(t => t.Length > 1)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableList();

    private static float[] BuildTfidfVector(ImmutableList<string> tokens, string[] vocabulary, float[] idf)
    {
        if (vocabulary.Length == 0 || tokens.IsEmpty)
        {
            return [];
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tokens)
        {
            counts.TryGetValue(t, out var c);
            counts[t] = c + 1;
        }

        var total = Math.Max(1, tokens.Count);
        var vector = new float[vocabulary.Length];
        for (var i = 0; i < vocabulary.Length; i++)
        {
            if (!counts.TryGetValue(vocabulary[i], out var tf))
            {
                continue;
            }

            vector[i] = (tf / (float)total) * idf[i];
        }

        return Normalize(vector);
    }

    private static float[] Normalize(float[] vector)
    {
        if (vector.Length == 0)
        {
            return vector;
        }

        double sum = 0;
        foreach (var v in vector)
        {
            sum += v * v;
        }

        if (sum <= 1e-12)
        {
            return vector;
        }

        var norm = Math.Sqrt(sum);
        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            result[i] = (float)(vector[i] / norm);
        }

        return result;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }

    private static double MetaMultiplier(MemoryItem m)
    {
        var imp = Math.Max(1, m.Importance);
        var hours = (DateTime.UtcNow - m.Timestamp.ToUniversalTime()).TotalHours;
        var rec = hours switch
        {
            < 24 => 1.28,
            < 24 * 7 => 1.1,
            < 24 * 30 => 1.03,
            _ => 1.0,
        };
        return (0.55 + imp * 0.15) * rec;
    }

    private static string NormalizeModel(string? model)
    {
        var m = (model ?? string.Empty).Trim();
        return m.Length == 0 ? "nomic-embed-text" : m;
    }

    private static string ResolveCachePath(string vaultPath)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HermesWpf",
            "embedding_cache");
        Directory.CreateDirectory(root);
        var key = string.IsNullOrWhiteSpace(vaultPath) ? "default" : HashText(vaultPath.ToLowerInvariant());
        return Path.Combine(root, key + ".json");
    }

    private static Dictionary<string, CacheEntry> LoadCache(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<CacheFile>(json);
            if (data?.Entries is null)
            {
                return new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            }

            return data.Entries.ToDictionary(
                kv => kv.Key,
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveCache(string path, Dictionary<string, CacheEntry> entries)
    {
        try
        {
            var file = new CacheFile
            {
                SavedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Entries = entries,
            };
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            // cache is optional
            _ = ex;
        }
    }

    private sealed class CacheFile
    {
        public string SavedAt { get; set; } = string.Empty;
        public Dictionary<string, CacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CacheEntry
    {
        public CacheEntry()
        {
        }

        public CacheEntry(string model, string contentHash, float[] vector)
        {
            Model = model;
            ContentHash = contentHash;
            Vector = vector;
        }

        public string Model { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
        public float[] Vector { get; set; } = [];
    }
}
