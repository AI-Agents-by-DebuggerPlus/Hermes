using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishTutorClient.Services;

public sealed class AzureSttResult
{
    public AzureSttResult(string status, string? displayText)
    {
        Status = status;
        DisplayText = displayText;
    }

    public string Status { get; }
    public string? DisplayText { get; }
    public bool Success =>
        string.Equals(Status, "Success", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(DisplayText);
}

/// <summary>Azure Speech REST STT (short audio ≤60s). Same key/endpoint as TTS.</summary>
public sealed class AzureSpeechSttClient
{
    private static readonly HttpClient Http;

    static AzureSpeechSttClient()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch
        {
            /* ignore */
        }

        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    }

    public static bool IsConfigured(AppSettings s) => AzureSpeechTtsClient.IsConfigured(s);

    public async Task<AzureSttResult> RecognizeWavAsync(
        AppSettings settings,
        byte[] wavBytes,
        string language,
        int sampleRateHz,
        CancellationToken ct)
    {
        if (wavBytes == null || wavBytes.Length < 44)
            return new AzureSttResult("NoMatch", null);

        var lang = string.IsNullOrWhiteSpace(language) ? "ru-RU" : language.Trim();
        var url = BuildRecognizeUrl(settings, lang);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", settings.AzureSpeechKey.Trim());
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.TryAddWithoutValidation("User-Agent", "Hermes.EnglishTutorClient");

        var content = new ByteArrayContent(wavBytes);
        // MediaTypeHeaderValue.Parse rejects "codecs=..." — send via TryAddWithoutValidation.
        content.Headers.TryAddWithoutValidation(
            "Content-Type",
            "audio/wav; codecs=audio/pcm; samplerate=" + sampleRateHz.ToString(CultureInfo.InvariantCulture));
        req.Content = content;

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Azure STT HTTP " + (int)resp.StatusCode + ": " + Truncate(body, 300));
        }

        var json = JObject.Parse(body);
        var status = (string?)json["RecognitionStatus"] ?? "Error";
        var text = (string?)json["DisplayText"];
        return new AzureSttResult(status, text);
    }

    public static string BuildRecognizeUrl(AppSettings s, string language)
    {
        var lang = Uri.EscapeDataString(language);
        if (!string.IsNullOrWhiteSpace(s.AzureSpeechEndpoint))
        {
            var baseUrl = s.AzureSpeechEndpoint.Trim().TrimEnd('/');
            // Endpoint may be resource root or already include /stt/...
            if (baseUrl.IndexOf("/speech/recognition/", StringComparison.OrdinalIgnoreCase) >= 0
                || baseUrl.IndexOf("/stt/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var sep = baseUrl.Contains("?") ? "&" : "?";
                return baseUrl + sep + "language=" + lang;
            }

            return baseUrl + "/stt/speech/recognition/conversation/cognitiveservices/v1?language=" + lang;
        }

        var region = s.AzureSpeechRegion.Trim();
        return "https://" + region + ".stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language="
               + lang;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");
}
