using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hermes.WpGallery;

namespace Hermes.WpGallery.Tool.Services;

/// <summary>Отправка логов на WordPress: POST /wp-json/hermes/v1/logs</summary>
public sealed class HermesLogSender : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly List<RemoteLogEntry> _buffer = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Timer? _timer;
    private bool _disposed;

    public string SiteUrl { get; }
    public string Token   { get; }
    public string Source  { get; set; } = "Hermes.WpGallery.Tool/1.0";
    public int    BatchSize { get; set; } = 50;
    public int    FlushIntervalSeconds { get; set; } = 30;

    public HermesLogSender(string siteUrl, string token, bool startTimer = true)
    {
        SiteUrl = siteUrl.TrimEnd('/');
        Token   = token;

        if (startTimer && FlushIntervalSeconds > 0)
        {
            var interval = TimeSpan.FromSeconds(FlushIntervalSeconds);
            _timer = new Timer(_ => _ = FlushAsync(), null, interval, interval);
        }
    }

    public void Log(string level, string message, string? category = null, string? exception = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        lock (_buffer)
        {
            _buffer.Add(new RemoteLogEntry
            {
                Level     = level,
                Message   = message,
                Timestamp = DateTime.UtcNow.ToString("o"),
                Category  = category ?? "",
                Exception = exception,
            });
        }

        if (_buffer.Count >= BatchSize)
            _ = FlushAsync();
    }

    public async Task<bool> FlushAsync(CancellationToken ct = default)
    {
        List<RemoteLogEntry> batch;
        lock (_buffer)
        {
            if (_buffer.Count == 0) return true;
            batch = new List<RemoteLogEntry>(_buffer);
            _buffer.Clear();
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!WpGalleryEndpoints.TryNormalizeSiteUrl(SiteUrl, out var site, out _))
                return false;

            var url = $"{site}/wp-json/hermes/v1/logs";
            var payload = new { token = Token, source = Source, entries = batch };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("X-Hermes-Token", Token);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        try { FlushAsync().GetAwaiter().GetResult(); }
        catch { /* ignore on exit */ }
        _lock.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class RemoteLogEntry
    {
        [JsonPropertyName("level")]     public string  Level     { get; set; } = "";
        [JsonPropertyName("message")]   public string  Message   { get; set; } = "";
        [JsonPropertyName("timestamp")] public string  Timestamp { get; set; } = "";
        [JsonPropertyName("category")]  public string? Category  { get; set; }
        [JsonPropertyName("exception")] public string? Exception { get; set; }
    }
}
