using System;

namespace Hermes.EnglishTutorClient.Services;

internal static class SttEnginePicker
{
    public const string Google = "Google";
    public const string Azure = "Azure";
    public const string Sapi = "SAPI";

    public static string Pick(AppSettings? settings)
    {
        if (settings != null && GoogleSpeechSttClient.IsConfigured(settings))
            return Google;
        if (settings != null && AzureSpeechSttClient.IsConfigured(settings))
            return Azure;
        return Sapi;
    }
}
