using System;
using System.Linq;
using System.Speech.Recognition;
using System.Threading;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>Shared InstalledRecognizers lookup with loud logging on culture mismatch.</summary>
internal static class SpeechRecognizerPicker
{
    public const float MinConfidence = 0.5f;
    private static int _loggedInstalled;

    public static void LogInstalledRecognizersOnce()
    {
        if (Interlocked.Exchange(ref _loggedInstalled, 1) != 0) return;
        try
        {
            var all = SpeechRecognitionEngine.InstalledRecognizers();
            if (all == null || all.Count == 0)
            {
                AppLog.Error("InstalledRecognizers: NONE");
                return;
            }

            var list = string.Join(" | ", all.Cast<RecognizerInfo>()
                .Select(r => r.Culture.Name + " [" + r.Description + "]"));
            AppLog.Info("InstalledRecognizers (" + all.Count + "): " + list);
        }
        catch (Exception ex)
        {
            AppLog.Error("InstalledRecognizers list failed: " + ex.Message);
        }
    }

    public static RecognizerInfo? Pick(string cultureReq)
    {
        LogInstalledRecognizersOnce();
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        if (installed == null || installed.Count == 0)
        {
            AppLog.Error("PickRecognizer: no recognizers installed at all");
            return null;
        }

        var req = string.IsNullOrWhiteSpace(cultureReq) ? "en-US" : cultureReq.Trim();
        var exact = installed.Cast<RecognizerInfo>()
            .FirstOrDefault(r => string.Equals(r.Culture.Name, req, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        var prefix = req.Split('-')[0];
        var sameLang = installed.Cast<RecognizerInfo>()
            .FirstOrDefault(r => r.Culture.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (sameLang != null)
        {
            if (Interlocked.Exchange(ref _loggedFallback, 1) == 0)
                AppLog.Error("PickRecognizer: no " + req + ", using " + sameLang.Culture.Name);
            return sameLang;
        }

        var fallback = installed.Cast<RecognizerInfo>().FirstOrDefault();
        if (Interlocked.Exchange(ref _loggedFallback, 1) == 0)
            AppLog.Error("PickRecognizer: no " + req + ", using " + (fallback?.Culture.Name ?? "NONE"));
        return fallback;
    }

    private static int _loggedFallback;
}
