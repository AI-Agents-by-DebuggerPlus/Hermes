using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hermes.EnglishLearning.Xp;

/// <summary>
/// HTTPS via curl.exe (OpenSSL). Used when Schannel/.NET cannot negotiate TLS 1.2 (typical on XP).
/// Looks for: BaseDir\tools\curl.exe, BaseDir\curl.exe, then PATH.
/// </summary>
internal static class CurlHttp
{
    private static string _resolved;
    private static bool _resolveAttempted;

    public static bool IsAvailable
    {
        get
        {
            EnsureResolved();
            return !string.IsNullOrEmpty(_resolved);
        }
    }

    public static string ExecutablePath
    {
        get
        {
            EnsureResolved();
            return _resolved;
        }
    }

    private static void EnsureResolved()
    {
        if (_resolveAttempted) return;
        _resolveAttempted = true;
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "tools", "curl.exe"),
                Path.Combine(baseDir, "curl.exe"),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    _resolved = Path.GetFullPath(c);
                    AppLog.Info("curl HTTPS transport: " + _resolved);
                    return;
                }
            }

            // On XP never trust PATH Schannel curl; on newer OS allow PATH as last resort.
            if (Environment.OSVersion.Version.Major >= 6)
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        var p = Path.Combine(dir.Trim(), "curl.exe");
                        if (File.Exists(p))
                        {
                            _resolved = p;
                            AppLog.Info("curl HTTPS transport (PATH): " + p);
                            return;
                        }
                    }
                    catch
                    {
                        // next
                    }
                }
            }

            AppLog.Warn("curl.exe not found — TLS1.2 Schannel-only (often fails on XP)");
        }
        catch (Exception ex)
        {
            AppLog.Warn("curl resolve: " + ex.Message);
        }
    }

    public static string Get(string url, string apikey, string bearer, int timeoutSec)
    {
        return Run(url, "GET", null, apikey, bearer, preferMinimal: false, timeoutSec);
    }

    public static string PostJson(string url, string body, string apikey, string bearer, bool preferMinimal, int timeoutSec)
    {
        return Run(url, "POST", body ?? "{}", apikey, bearer, preferMinimal, timeoutSec);
    }

    private static string Run(
        string url,
        string method,
        string body,
        string apikey,
        string bearer,
        bool preferMinimal,
        int timeoutSec)
    {
        EnsureResolved();
        if (string.IsNullOrEmpty(_resolved))
            throw new InvalidOperationException("curl.exe not found");

        var args = new StringBuilder();
        args.Append("-sS -L --http1.1 -f ");
        args.Append("--connect-timeout ").Append(Math.Max(5, timeoutSec / 2)).Append(' ');
        args.Append("--max-time ").Append(Math.Max(10, timeoutSec)).Append(' ');
        try
        {
            var ca = Path.Combine(Path.GetDirectoryName(_resolved) ?? string.Empty, "curl-ca-bundle.crt");
            if (File.Exists(ca))
                args.Append("--cacert \"").Append(ca).Append("\" ");
        }
        catch
        {
            // ignore
        }

        args.Append("-X ").Append(method).Append(' ');
        args.Append("-H \"apikey: ").Append(Escape(apikey)).Append("\" ");
        args.Append("-H \"Authorization: Bearer ").Append(Escape(bearer)).Append("\" ");
        args.Append("-H \"Accept: application/json\" ");
        if (preferMinimal)
            args.Append("-H \"Prefer: return=minimal\" ");

        string bodyFile = null;
        try
        {
            if (body != null)
            {
                bodyFile = Path.Combine(Path.GetTempPath(), "el_xp_" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(bodyFile, body, new UTF8Encoding(false));
                args.Append("-H \"Content-Type: application/json\" ");
                args.Append("--data-binary @\"").Append(bodyFile).Append("\" ");
            }

            args.Append('"').Append(url.Replace("\"", "%22")).Append('"');

            var psi = new ProcessStartInfo
            {
                FileName = _resolved,
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_resolved) ?? AppDomain.CurrentDomain.BaseDirectory,
            };

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    throw new InvalidOperationException("curl failed to start");

                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(Math.Max(15, timeoutSec + 5) * 1000))
                {
                    try { p.Kill(); } catch { /* ignore */ }
                    throw new TimeoutException("curl timeout");
                }

                if (p.ExitCode != 0)
                {
                    var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    throw new InvalidOperationException("curl exit=" + p.ExitCode + " " + Truncate(err, 200));
                }

                return stdout ?? string.Empty;
            }
        }
        finally
        {
            if (bodyFile != null)
            {
                try { File.Delete(bodyFile); } catch { /* ignore */ }
            }
        }
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\"", "'");
    }

    private static string Truncate(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}
