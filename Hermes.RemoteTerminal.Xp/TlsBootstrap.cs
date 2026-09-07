using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Hermes.RemoteTerminal.Xp;

/// <summary>
/// Force TLS 1.1/1.2 for Supabase/Cloudflare. On stock XP Schannel often has no TLS 1.2 —
/// then <see cref="XpHttp"/> falls back to curl.exe (OpenSSL).
/// </summary>
internal static class TlsBootstrap
{
    // Enum values may be missing on .NET 4.0 RTM — use numeric casts.
    private const SecurityProtocolType Tls12 = (SecurityProtocolType)3072;
    private const SecurityProtocolType Tls11 = (SecurityProtocolType)768;
    private const SecurityProtocolType Tls10 = (SecurityProtocolType)192;

    public static bool Tls12Enabled { get; private set; }

    public static void Apply()
    {
        ServicePointManager.Expect100Continue = false;
        ServicePointManager.DefaultConnectionLimit = 8;
        ServicePointManager.CheckCertificateRevocationList = false;
        ServicePointManager.UseNagleAlgorithm = false;

        // Prefer modern suites; keep Tls10 as last-resort for ancient endpoints.
        var desired = Tls12 | Tls11 | Tls10;
        try
        {
            ServicePointManager.SecurityProtocol = desired;
        }
        catch (Exception ex)
        {
            AppLog.Warn("TLS set(Tls12|Tls11|Tls10) failed: " + ex.Message);
            try
            {
                ServicePointManager.SecurityProtocol = Tls12 | Tls10;
            }
            catch (Exception ex2)
            {
                AppLog.Warn("TLS set(Tls12|Tls10) failed: " + ex2.Message);
                try
                {
                    // Last resort: OR into whatever the runtime accepts.
                    ServicePointManager.SecurityProtocol |= Tls12;
                }
                catch (Exception ex3)
                {
                    AppLog.Warn("TLS OR Tls12 failed: " + ex3.Message);
                }
            }
        }

        try
        {
            var current = ServicePointManager.SecurityProtocol;
            Tls12Enabled = ((int)current & (int)Tls12) != 0;
            AppLog.Info("SecurityProtocol=" + current + " Tls12Enabled=" + Tls12Enabled);
            if (!Tls12Enabled)
            {
                AppLog.Warn(
                    "TLS 1.2 not active in CLR. On XP install POSReady TLS 1.2 / Easy Fix, "
                    + "or place tools\\curl.exe (OpenSSL build) next to the app.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("SecurityProtocol read: " + ex.Message);
        }

        try
        {
            // Log cert errors without accepting invalid certs by default.
            ServicePointManager.ServerCertificateValidationCallback = ValidateServerCert;
        }
        catch (Exception ex)
        {
            AppLog.Warn("ServerCertificateValidationCallback: " + ex.Message);
        }
    }

    private static bool ValidateServerCert(
        object sender,
        X509Certificate certificate,
        X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        AppLog.Warn("TLS cert error: " + sslPolicyErrors
            + " subject=" + (certificate != null ? certificate.Subject : "?"));

        // XP root store is ancient — allow chain errors for Supabase/Cloudflare hostnames only.
        var isXp = Environment.OSVersion.Version.Major < 6;
        if (isXp
            && (sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) == 0
            && certificate != null)
        {
            var subject = certificate.Subject ?? string.Empty;
            if (subject.IndexOf("supabase", StringComparison.OrdinalIgnoreCase) >= 0
                || subject.IndexOf("cloudflare", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AppLog.Warn("Accepting chain error on XP (outdated roots)");
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeTlsFailure(Exception ex)
    {
        if (ex == null) return false;
        var msg = ex.ToString();
        if (string.IsNullOrEmpty(msg)) return false;
        return msg.IndexOf("underlying connection was closed", StringComparison.OrdinalIgnoreCase) >= 0
               || msg.IndexOf("Could not create SSL/TLS secure channel", StringComparison.OrdinalIgnoreCase) >= 0
               || msg.IndexOf("SendFailure", StringComparison.OrdinalIgnoreCase) >= 0
               || msg.IndexOf("Authentication failed", StringComparison.OrdinalIgnoreCase) >= 0
               || msg.IndexOf("secure channel", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
