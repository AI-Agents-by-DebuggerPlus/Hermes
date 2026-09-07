using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace Hermes.EnglishLearning.Xp;

/// <summary>Internet / TLS / Supabase reachability checks with AppLog output.</summary>
internal static class NetworkDiagnostics
{
    public static void Run(AppSettings settings)
    {
        AppLog.Info("=== Network diagnostics START ===");
        AppLog.Info("OS=" + SystemInfo.Describe());
        try
        {
            AppLog.Info("SecurityProtocol=" + ServicePointManager.SecurityProtocol);
        }
        catch (Exception ex)
        {
            AppLog.Warn("SecurityProtocol read: " + ex.Message);
        }

        var nicOk = false;
        try
        {
            nicOk = NetworkInterface.GetIsNetworkAvailable();
            AppLog.Info("NetworkInterface.GetIsNetworkAvailable=" + nicOk);
        }
        catch (Exception ex)
        {
            AppLog.Warn("GetIsNetworkAvailable: " + ex.Message);
        }

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                AppLog.Info("NIC up: " + nic.Name + " type=" + nic.NetworkInterfaceType
                    + " speed=" + nic.Speed);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Enumerate NICs: " + ex.Message);
        }

        ProbeDns("dns.msftncsi.com");
        ProbeHttp("http://www.msftncsi.com/ncsi.txt", "NCSI HTTP");
        ProbeHttp("http://connectivitycheck.gstatic.com/generate_204", "Google 204");

        var baseUrl = settings != null ? (settings.SupabaseUrl ?? string.Empty).Trim().TrimEnd('/') : string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            AppLog.Warn("SupabaseUrl empty — skip Supabase probe");
        }
        else
        {
            Uri uri;
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out uri))
            {
                ProbeDns(uri.Host);
                // Skip empty REST root — it often returns 401 without proving anything.
                if (!string.IsNullOrWhiteSpace(settings.SupabaseAnonKey))
                {
                    ProbeSupabaseMessages(settings);
                }
                else
                {
                    AppLog.Warn("SupabaseAnonKey empty — skip authenticated REST probe");
                }
            }
            else
            {
                AppLog.Error("Invalid SupabaseUrl: " + baseUrl);
            }
        }

        AppLog.Info("=== Network diagnostics END (networkAvailable=" + nicOk + ") ===");
    }

    private static void ProbeDns(string host)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var addrs = Dns.GetHostAddresses(host);
            sw.Stop();
            var sb = new StringBuilder();
            for (var i = 0; i < addrs.Length && i < 4; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(addrs[i]);
            }

            AppLog.Info("DNS " + host + " OK " + sw.ElapsedMilliseconds + "ms → " + sb);
        }
        catch (Exception ex)
        {
            AppLog.Error("DNS " + host + " FAIL: " + ex.Message);
        }
    }

    private static void ProbeHttp(string url, string label)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 12000;
            req.ReadWriteTimeout = 12000;
            req.AllowAutoRedirect = true;
            req.UserAgent = "Hermes.EnglishLearning.Xp/1.0";
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                sw.Stop();
                AppLog.Info(label + " OK HTTP " + (int)resp.StatusCode + " " + sw.ElapsedMilliseconds + "ms url=" + url);
            }
        }
        catch (WebException wex)
        {
            var code = "?";
            try
            {
                var r = wex.Response as HttpWebResponse;
                if (r != null) code = ((int)r.StatusCode).ToString();
            }
            catch { /* ignore */ }
            AppLog.Error(label + " FAIL status=" + code + " " + wex.Status + ": " + wex.Message + " url=" + url);
        }
        catch (Exception ex)
        {
            AppLog.Error(label + " FAIL: " + ex.Message + " url=" + url);
        }
    }

    private static void ProbeSupabaseMessages(AppSettings settings)
    {
        var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = settings.SupabaseAnonKey.Trim();
        var url = baseUrl + "/rest/v1/messages?select=id&limit=1";
        try
        {
            var sw = Stopwatch.StartNew();
            var body = XpHttp.Get(url, anon, anon, 15000);
            sw.Stop();
            AppLog.Info("Supabase messages OK " + sw.ElapsedMilliseconds + "ms bodyLen="
                + (body != null ? body.Length : 0));
        }
        catch (Exception ex)
        {
            AppLog.Error("Supabase messages FAIL: " + ex.Message);
            if (TlsBootstrap.LooksLikeTlsFailure(ex))
            {
                AppLog.Warn("Hint: need TLS 1.2 (POSReady/Easy Fix) or tools\\curl.exe (OpenSSL) beside the app.");
            }
        }
    }

    /// <summary>Run on background thread; UI should BeginInvoke status updates.</summary>
    public static void RunAsync(AppSettings settings, Action onDone)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Run(settings); }
            catch (Exception ex) { AppLog.Error("Diagnostics crash: " + ex.Message); }
            finally
            {
                if (onDone != null)
                {
                    try { onDone(); } catch { /* ignore */ }
                }
            }
        });
    }
}
