using System;
using System.IO;
using System.Threading;
using Hermes.EnglishTutorClient.Services;

namespace Hermes.EnglishTutorClient;

/// <summary>Headless live-STT check.</summary>
internal static class SttSelfTest
{
    public static int Run(string? headsetHint)
    {
        AppLog.LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hermes.EnglishTutorClient", "logs");
        AppLog.Info("=== STT self-test begin hint=" + (headsetHint ?? "(none)") + " ===");

        var voice = new VoiceInputService();
        try
        {
            voice.PartialResult += t => AppLog.Info("STT self-test partial: " + t);
            voice.Start("en-US", headsetHint ?? "Pixel Buds");
            if (!voice.IsListening)
            {
                AppLog.Error("STT self-test: failed to start listening");
                return 2;
            }

            AppLog.Info("STT self-test: listening 3s — speak now…");
            Thread.Sleep(3000);
            var text = voice.StopAndTakeText();
            AppLog.Info("STT self-test result: '" + (text ?? "") + "'");
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("STT self-test exception: " + ex);
            return 1;
        }
        finally
        {
            voice.Dispose();
            AppLog.Info("=== STT self-test end ===");
        }
    }
}
