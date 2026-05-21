using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>HTTP client for Ollama /api/embeddings and /api/embed.</summary>
public sealed class OllamaEmbeddingClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly LogService? _log;

    public OllamaEmbeddingClient(string baseUrl, LogService? log = null, HttpMessageHandler? handler = null)
    {
        _log = log;
        var root = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (root.Length == 0)
        {
            root = "http://127.0.0.1:11434";
        }

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(90);
        _http.BaseAddress = new Uri(root + "/");
    }

    public async Task<float[]?> TryEmbedAsync(string model, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var m = (model ?? string.Empty).Trim();
        if (m.Length == 0)
        {
            m = "nomic-embed-text";
        }

        var fromEmbed = await TryPostEmbedAsync(m, text, cancellationToken).ConfigureAwait(false);
        if (fromEmbed is not null)
        {
            return fromEmbed;
        }

        return await TryPostEmbeddingsAsync(m, text, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    private async Task<float[]?> TryPostEmbedAsync(string model, string text, CancellationToken cancellationToken)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { model, input = text });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("api/embed", content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseEmbeddingDocument(doc);
        }
        catch (Exception ex)
        {
            _log?.LogWarn($"[vector-memory] Ollama /api/embed: {ex.Message}");
            return null;
        }
    }

    private async Task<float[]?> TryPostEmbeddingsAsync(string model, string text, CancellationToken cancellationToken)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { model, prompt = text });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("api/embeddings", content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseEmbeddingDocument(doc);
        }
        catch (Exception ex)
        {
            _log?.LogWarn($"[vector-memory] Ollama /api/embeddings: {ex.Message}");
            return null;
        }
    }

    private static float[]? ParseEmbeddingDocument(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("embedding", out var single) && single.ValueKind == JsonValueKind.Array)
        {
            return ReadFloatArray(single);
        }

        if (doc.RootElement.TryGetProperty("embeddings", out var many) && many.ValueKind == JsonValueKind.Array && many.GetArrayLength() > 0)
        {
            var first = many[0];
            if (first.ValueKind == JsonValueKind.Array)
            {
                return ReadFloatArray(first);
            }
        }

        return null;
    }

    private static float[] ReadFloatArray(JsonElement array)
    {
        var len = array.GetArrayLength();
        var result = new float[len];
        var i = 0;
        foreach (var el in array.EnumerateArray())
        {
            result[i++] = (float)el.GetDouble();
        }

        return result;
    }
}
