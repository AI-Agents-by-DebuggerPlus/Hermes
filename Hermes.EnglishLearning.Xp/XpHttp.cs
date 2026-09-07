using System;
using System.IO;
using System.Net;
using System.Text;

namespace Hermes.EnglishLearning.Xp;

/// <summary>
/// HTTPS helper: Schannel WebRequest first; on TLS failure (or on XP when curl is present), use curl/OpenSSL.
/// </summary>
internal static class XpHttp
{
    private static bool _preferCurl;
    private static bool _loggedMode;

    public static string Get(string url, string apikey, string bearer, int timeoutMs = 25000)
    {
        return Send("GET", url, null, apikey, bearer, preferMinimal: false, timeoutMs);
    }

    public static string PostJson(
        string url,
        string body,
        string apikey,
        string bearer,
        bool preferMinimal = false,
        int timeoutMs = 25000)
    {
        return Send("POST", url, body ?? "{}", apikey, bearer, preferMinimal, timeoutMs);
    }

    private static string Send(
        string method,
        string url,
        string body,
        string apikey,
        string bearer,
        bool preferMinimal,
        int timeoutMs)
    {
        var isXp = Environment.OSVersion.Version.Major < 6;
        // On XP always prefer bundled OpenSSL curl when present (Schannel has no TLS 1.2).
        var useCurlFirst = _preferCurl
                           || (CurlHttp.IsAvailable && (isXp || !TlsBootstrap.Tls12Enabled));
        if (!_loggedMode)
        {
            _loggedMode = true;
            AppLog.Info("XpHttp mode: OS=" + SystemInfo.FriendlyOsName()
                + " Tls12=" + TlsBootstrap.Tls12Enabled
                + " curl=" + CurlHttp.IsAvailable
                + " preferCurl=" + useCurlFirst);
        }
        if (useCurlFirst && CurlHttp.IsAvailable)
        {
            return CurlHttpCall(method, url, body, apikey, bearer, preferMinimal, timeoutMs);
        }

        try
        {
            return WebRequestCall(method, url, body, apikey, bearer, preferMinimal, timeoutMs);
        }
        catch (Exception ex)
        {
            if (!CurlHttp.IsAvailable || !TlsBootstrap.LooksLikeTlsFailure(ex))
                throw;

            AppLog.Warn("Schannel HTTPS failed → curl: " + Truncate(ex.Message, 120));
            _preferCurl = true;
            return CurlHttpCall(method, url, body, apikey, bearer, preferMinimal, timeoutMs);
        }
    }

    private static string CurlHttpCall(
        string method,
        string url,
        string body,
        string apikey,
        string bearer,
        bool preferMinimal,
        int timeoutMs)
    {
        var sec = Math.Max(10, timeoutMs / 1000);
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            return CurlHttp.Get(url, apikey, bearer, sec);
        return CurlHttp.PostJson(url, body, apikey, bearer, preferMinimal, sec);
    }

    private static string WebRequestCall(
        string method,
        string url,
        string body,
        string apikey,
        string bearer,
        bool preferMinimal,
        int timeoutMs)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = method;
        req.Timeout = timeoutMs;
        req.ReadWriteTimeout = timeoutMs;
        req.KeepAlive = false;
        req.ProtocolVersion = HttpVersion.Version11;
        req.Accept = "application/json";
        req.Headers["apikey"] = apikey;
        req.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;

        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "{}");
            req.ContentType = "application/json";
            req.ContentLength = bytes.Length;
            if (preferMinimal)
                req.Headers["Prefer"] = "return=minimal";
            using (var s = req.GetRequestStream())
                s.Write(bytes, 0, bytes.Length);
        }

        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var stream = resp.GetResponseStream())
        {
            if (stream == null) return string.Empty;
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }

    private static string Truncate(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}
