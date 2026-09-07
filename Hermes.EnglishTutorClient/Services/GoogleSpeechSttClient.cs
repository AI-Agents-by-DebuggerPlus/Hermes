using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishTutorClient.Services;

public sealed class GoogleSttResult
{
    public GoogleSttResult(string status, string? transcript, float confidence)
    {
        Status = status;
        Transcript = transcript;
        Confidence = confidence;
    }

    public string Status { get; }
    public string? Transcript { get; }
    public float Confidence { get; }
    public bool Success =>
        string.Equals(Status, "Success", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Transcript);
}

/// <summary>Google Cloud Speech-to-Text v1 (sync recognize, short audio).</summary>
public sealed class GoogleSpeechSttClient
{
    private const string RecognizeUrl = "https://speech.googleapis.com/v1/speech:recognize";
    private static readonly HttpClient Http;

    static GoogleSpeechSttClient()
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

    public static bool IsConfigured(AppSettings s) =>
        s != null && !string.IsNullOrWhiteSpace(s.GoogleSpeechApiKey);

    public async Task<GoogleSttResult> RecognizePcm16Async(
        AppSettings settings,
        byte[] pcm16Mono,
        int sampleRateHz,
        string language,
        CancellationToken ct)
    {
        if (pcm16Mono == null || pcm16Mono.Length < 2)
            return new GoogleSttResult("NoMatch", null, 0);

        var lang = string.IsNullOrWhiteSpace(language) ? "ru-RU" : language.Trim();
        var key = settings.GoogleSpeechApiKey.Trim();
        var url = RecognizeUrl + "?key=" + Uri.EscapeDataString(key);

        var body = new JObject
        {
            ["config"] = new JObject
            {
                ["encoding"] = "LINEAR16",
                ["sampleRateHertz"] = sampleRateHz,
                ["languageCode"] = lang,
                ["enableAutomaticPunctuation"] = true,
                ["model"] = "default",
            },
            ["audio"] = new JObject
            {
                ["content"] = Convert.ToBase64String(pcm16Mono),
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Hermes.EnglishTutorClient");
        req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var jsonText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Google STT HTTP " + (int)resp.StatusCode + ": " + Truncate(jsonText, 300));
        }

        var json = JObject.Parse(string.IsNullOrWhiteSpace(jsonText) ? "{}" : jsonText);
        var results = json["results"] as JArray;
        if (results == null || results.Count == 0)
            return new GoogleSttResult("NoMatch", null, 0);

        var sb = new StringBuilder();
        var bestConf = 0f;
        foreach (var r in results)
        {
            var alts = r["alternatives"] as JArray;
            var alt = alts != null && alts.Count > 0 ? alts[0] as JObject : null;
            if (alt == null) continue;
            var t = ((string?)alt["transcript"] ?? string.Empty).Trim();
            if (t.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(t);
            if (alt["confidence"] != null
                && float.TryParse(alt["confidence"]!.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var c)
                && c > bestConf)
            {
                bestConf = c;
            }
        }

        var text = sb.ToString().Trim();
        return text.Length == 0
            ? new GoogleSttResult("NoMatch", null, 0)
            : new GoogleSttResult("Success", text, bestConf);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");
}
