using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishLearning.Services;

public sealed class AzureVoiceInfo
{
    public AzureVoiceInfo(string shortName, string locale, string displayName)
    {
        ShortName = shortName;
        Locale = locale;
        DisplayName = displayName;
    }

    public string ShortName { get; }
    public string Locale { get; }
    public string DisplayName { get; }
    public string Name => DisplayName + " (" + ShortName + ")";
}

public sealed class AzureSpeechTtsClient
{
    private static readonly HttpClient Http;

    static AzureSpeechTtsClient()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch
        {
        }

        Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    }

    public static bool IsConfigured(AppSettings s) =>
        s != null &&
        !string.IsNullOrWhiteSpace(s.AzureSpeechKey) &&
        (!string.IsNullOrWhiteSpace(s.AzureSpeechEndpoint) || !string.IsNullOrWhiteSpace(s.AzureSpeechRegion));

    public async Task<byte[]> SynthesizeAsync(
        AppSettings settings,
        string text,
        string voice,
        string locale,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<byte>();
        }

        var url = BuildSynthesizeUrl(settings);
        var ssml = BuildSsml(text, voice, locale);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", settings.AzureSpeechKey.Trim());
        req.Headers.Add("X-Microsoft-OutputFormat", "audio-16khz-128kbitrate-mono-mp3");
        req.Headers.Add("User-Agent", "Hermes.EnglishLearning");
        req.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException(
                "Azure TTS HTTP " + (int)resp.StatusCode + ": " + Truncate(body, 300));
        }

        return bytes;
    }

    public async Task<IReadOnlyList<AzureVoiceInfo>> ListVoicesAsync(AppSettings settings, CancellationToken ct)
    {
        var url = BuildVoicesUrl(settings);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", settings.AzureSpeechKey.Trim());
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Azure voices HTTP " + (int)resp.StatusCode + ": " + Truncate(json, 300));
        }

        var arr = JArray.Parse(json);
        var list = new List<AzureVoiceInfo>();
        foreach (var item in arr)
        {
            var shortName = (string?)item["ShortName"] ?? string.Empty;
            var locale = (string?)item["Locale"] ?? string.Empty;
            var display = (string?)item["DisplayName"] ?? shortName;
            if (string.IsNullOrWhiteSpace(shortName))
            {
                continue;
            }

            if (!shortName.EndsWith("Neural", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                && !locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            list.Add(new AzureVoiceInfo(shortName, locale, display));
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static IReadOnlyList<AzureVoiceInfo> FallbackVoices()
    {
        return new[]
        {
            new AzureVoiceInfo("en-US-JennyNeural", "en-US", "Jenny"),
            new AzureVoiceInfo("en-US-AriaNeural", "en-US", "Aria"),
            new AzureVoiceInfo("en-US-GuyNeural", "en-US", "Guy"),
            new AzureVoiceInfo("en-GB-SoniaNeural", "en-GB", "Sonia"),
            new AzureVoiceInfo("ru-RU-SvetlanaNeural", "ru-RU", "Svetlana"),
            new AzureVoiceInfo("ru-RU-DmitryNeural", "ru-RU", "Dmitry"),
        };
    }

    private static string BuildSynthesizeUrl(AppSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.AzureSpeechEndpoint))
        {
            return s.AzureSpeechEndpoint.Trim().TrimEnd('/') + "/tts/cognitiveservices/v1";
        }

        return "https://" + s.AzureSpeechRegion.Trim() + ".tts.speech.microsoft.com/cognitiveservices/v1";
    }

    private static string BuildVoicesUrl(AppSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.AzureSpeechEndpoint))
        {
            return s.AzureSpeechEndpoint.Trim().TrimEnd('/') + "/tts/cognitiveservices/voices/list";
        }

        return "https://" + s.AzureSpeechRegion.Trim() + ".tts.speech.microsoft.com/cognitiveservices/voices/list";
    }

    private static string BuildSsml(string text, string voice, string locale)
    {
        var lang = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale.Trim();
        var voiceName = string.IsNullOrWhiteSpace(voice) ? "en-US-JennyNeural" : voice.Trim();
        var speak = new XElement(
            XName.Get("speak", "http://www.w3.org/2001/10/synthesis"),
            new XAttribute(XNamespace.Xml + "lang", lang),
            new XAttribute("version", "1.0"),
            new XElement("voice", new XAttribute("name", voiceName), text));
        return speak.ToString(SaveOptions.DisableFormatting);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");
}
