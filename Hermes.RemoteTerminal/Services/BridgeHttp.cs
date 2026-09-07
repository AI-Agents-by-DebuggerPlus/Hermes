using System.Net;
using System.Net.Http;
using System.Text;

namespace Hermes.RemoteTerminal.Services;

/// <summary>
/// REST transport for the backup bridge: HttpClient first, curl/OpenSSL fallback (RemoteTerminal.Xp channel).
/// </summary>
internal static class BridgeHttp
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static bool _preferCurl;
    private static bool _loggedMode;

    public static async Task<string> GetAsync(string url, string apikey, string bearer, CancellationToken ct = default) =>
        await SendAsync("GET", url, null, apikey, bearer, preferMinimal: false, ct).ConfigureAwait(false);

    public static async Task<string> PostJsonAsync(
        string url,
        string body,
        string apikey,
        string bearer,
        bool preferMinimal = false,
        CancellationToken ct = default) =>
        await SendAsync("POST", url, body, apikey, bearer, preferMinimal, ct).ConfigureAwait(false);

    private static async Task<string> SendAsync(
        string method,
        string url,
        string? body,
        string apikey,
        string bearer,
        bool preferMinimal,
        CancellationToken ct)
    {
        if (!_loggedMode)
        {
            _loggedMode = true;
            AppLog.Info("REST backup bridge: HttpClient + curl=" + CurlHttp.IsAvailable);
        }

        if (_preferCurl && CurlHttp.IsAvailable)
        {
            return CurlCall(method, url, body, apikey, bearer, preferMinimal);
        }

        try
        {
            return await HttpClientCallAsync(method, url, body, apikey, bearer, preferMinimal, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (CurlHttp.IsAvailable && LooksLikeTlsFailure(ex))
        {
            AppLog.Warn("REST backup HttpClient failed → curl: " + Truncate(ex.Message, 120));
            _preferCurl = true;
            return CurlCall(method, url, body, apikey, bearer, preferMinimal);
        }
    }

    private static async Task<string> HttpClientCallAsync(
        string method,
        string url,
        string? body,
        string apikey,
        string bearer,
        bool preferMinimal,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        req.Headers.TryAddWithoutValidation("apikey", apikey);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (preferMinimal)
        {
            req.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        }

        if (body is not null)
        {
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(text, 120)}");
        }

        return text;
    }

    private static string CurlCall(
        string method,
        string url,
        string? body,
        string apikey,
        string bearer,
        bool preferMinimal)
    {
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return CurlHttp.Get(url, apikey, bearer, 25);
        }

        return CurlHttp.PostJson(url, body ?? "{}", apikey, bearer, preferMinimal, 25);
    }

    private static bool LooksLikeTlsFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException or WebException)
            {
                var msg = e.Message ?? string.Empty;
                if (msg.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("secure channel", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= n ? s : s[..n] + "…");
}
