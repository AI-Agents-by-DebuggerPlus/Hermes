using System;
using System.Globalization;
using System.Speech.Synthesis;
using System.Threading.Tasks;

namespace Hermes.EnglishLearning.Services;

/// <summary>One-shot startup greeting on a private synthesizer (does not affect lesson TTS state).</summary>
public static class StartupGreeting
{
    /// <summary>EN: «EnglishLearning готово к работе.»</summary>
    public const string EnglishPhrase = "EnglishLearning is ready to work.";

    public static void SpeakAsync(AppSettings settings)
    {
        var volume = Math.Max(0, Math.Min(100, settings?.VolumePercent ?? 80));
        var voice = settings?.EnglishVoiceName ?? string.Empty;
        Task.Run(() =>
        {
            try
            {
                using var synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();
                synth.Volume = volume;
                synth.Rate = -1;
                if (!string.IsNullOrWhiteSpace(voice))
                {
                    try
                    {
                        synth.SelectVoice(voice);
                    }
                    catch
                    {
                        TrySelectEnglish(synth);
                    }
                }
                else
                {
                    TrySelectEnglish(synth);
                }

                AppLog.Info("Startup greeting: " + EnglishPhrase);
                synth.Speak(EnglishPhrase);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Startup greeting failed: " + ex.Message);
            }
        });
    }

    private static void TrySelectEnglish(SpeechSynthesizer synth)
    {
        try
        {
            synth.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult, 0, new CultureInfo("en-US"));
        }
        catch
        {
            // keep default
        }
    }
}
