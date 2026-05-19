using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.WpGallery;

/// <summary>REST client for WordPress plugin hermes-image-receiver (POST /message + SSE gallery).</summary>
public sealed class WpGalleryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public WpGalleryClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _ownsHttp = http is null;
    }

    /// <summary>Upload image via POST /hermes/v1/message (type, sender, image_base64).</summary>
    public async Task<WpGalleryUploadResult> UploadAsync(
        WpGalleryImageFrame frame,
        WpGalleryUploadOptions options,
        CancellationToken cancellationToken = default)
    {
        var messageUrl = WpGalleryEndpoints.BuildMessageUrl(options.SiteOrImageEndpoint);
        if (string.IsNullOrEmpty(messageUrl))
        {
            return new WpGalleryUploadResult(false, "URL сайта не задан или некорректен");
        }

        var sender = WpGalleryEndpoints.EffectiveSender(options.Sender ?? options.Channel);
        var sw = Stopwatch.StartNew();
        var attempt = 0;
        var maxRetries = Math.Max(1, options.MaxRetries);

        while (true)
        {
            attempt++;
            try
            {
                var payload = new MessagePayload
                {
                    Type = "image",
                    Sender = sender,
                    ImageBase64 = Convert.ToBase64String(frame.Data),
                };

                var json = JsonSerializer.Serialize(payload, JsonOptions);
                using var request = new HttpRequestMessage(HttpMethod.Post, messageUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var (url, id, success) = TryParseMessageResponse(body);
                    if (!success)
                    {
                        var err = TryParseError(body) ?? "Сервер вернул success=false";
                        if (attempt >= maxRetries)
                        {
                            return new WpGalleryUploadResult(false, err, BytesSent: frame.Data.Length, ElapsedMs: sw.Elapsed.TotalMilliseconds);
                        }

                        await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return new WpGalleryUploadResult(
                        true,
                        "Загружено успешно",
                        url,
                        id,
                        frame.Data.Length,
                        sw.Elapsed.TotalMilliseconds);
                }

                var errMsg = TryParseError(body) ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                if (attempt >= maxRetries)
                {
                    return new WpGalleryUploadResult(
                        false,
                        $"Ошибка: {errMsg}",
                        BytesSent: frame.Data.Length,
                        ElapsedMs: sw.Elapsed.TotalMilliseconds);
                }

                await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new WpGalleryUploadResult(false, "Отменено");
            }
            catch (Exception ex)
            {
                if (attempt >= maxRetries)
                {
                    return new WpGalleryUploadResult(false, $"Ошибка сети: {ex.Message}");
                }

                await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<WpGalleryConnectionTestResult> TestConnectionAsync(
        WpGalleryConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!WpGalleryEndpoints.TryNormalizeSiteUrl(options.SiteOrImageEndpoint, out var siteUrl, out var urlError))
        {
            return new WpGalleryConnectionTestResult(false, urlError ?? "URL сайта не задан");
        }

        var messageUrl = $"{siteUrl}{WpGalleryEndpoints.MessagePath}";
        var streamUrl = $"{siteUrl}{WpGalleryEndpoints.StreamPath}";
        var statusUrl = $"{siteUrl}{WpGalleryEndpoints.StatusPath}";
        var sender = WpGalleryEndpoints.EffectiveSender(options.Sender ?? options.Channel);

        try
        {
            string version = "?";
            using (var statusResp = await _http.GetAsync(statusUrl, cancellationToken).ConfigureAwait(false))
            {
                if (statusResp.IsSuccessStatusCode)
                {
                    var statusBody = await statusResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        using var doc = JsonDocument.Parse(statusBody);
                        version = doc.RootElement.TryGetProperty("version", out var v) ? v.ToString() : "?";
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            var probe = JsonSerializer.Serialize(new MessagePayload
            {
                Type = "text",
                Sender = sender,
                Message = "Hermes.Wpf connection test",
            }, JsonOptions);

            using var probeRequest = new HttpRequestMessage(HttpMethod.Post, messageUrl)
            {
                Content = new StringContent(probe, Encoding.UTF8, "application/json"),
            };

            using var probeResp = await _http.SendAsync(probeRequest, cancellationToken).ConfigureAwait(false);
            var probeBody = await probeResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (probeResp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new WpGalleryConnectionTestResult(
                    false,
                    "REST /wp-json/hermes/v1/message не найден. Установите плагин Hermes Image Receiver v1.0.6+.");
            }

            if (!probeResp.IsSuccessStatusCode)
            {
                return new WpGalleryConnectionTestResult(
                    false,
                    $"HTTP {(int)probeResp.StatusCode} — {TryParseError(probeBody) ?? probeBody}");
            }

            return new WpGalleryConnectionTestResult(
                true,
                $"Hermes Receiver v{version}. Отправитель: «{sender}». SSE: {streamUrl}. " +
                $"Шорткод: [hermes_gallery channel=\"{sender}\"]");
        }
        catch (Exception ex)
        {
            return new WpGalleryConnectionTestResult(false, $"Ошибка сети: {ex.Message}");
        }
    }

    private static (string Url, int Id, bool Success) TryParseMessageResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var success = !root.TryGetProperty("success", out var s) || s.GetBoolean();
            var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var id = root.TryGetProperty("id", out var i) && i.TryGetInt32(out var n) ? n : 0;
            return (url, id, success);
        }
        catch
        {
            return ("", 0, true);
        }
    }

    private static string? TryParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                return err.GetString();
            }

            if (root.TryGetProperty("message", out var msg))
            {
                return msg.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private sealed class MessagePayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "text";

        [JsonPropertyName("sender")]
        public string Sender { get; init; } = "";

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("image_base64")]
        public string? ImageBase64 { get; init; }
    }
}
