using System;
using System.Speech.Synthesis;
using System.Globalization;
using Hermes.EnglishLearning.Models;

namespace Hermes.EnglishLearning.Services;

/// <summary>Local TTS via Windows SAPI (no audio over Supabase).</summary>
public sealed class LocalTtsService : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    public LocalTtsService()
    {
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Rate = -1;
    }

    public void SpeakCard(CardPair card, bool englishFirst = true)
    {
        if (card == null)
        {
            return;
        }

        _synth.SpeakAsyncCancelAll();
        var pb = new PromptBuilder();
        if (englishFirst)
        {
            Append(pb, card.En, "en-US");
            Append(pb, card.Ru, "ru-RU");
        }
        else
        {
            Append(pb, card.Ru, "ru-RU");
            Append(pb, card.En, "en-US");
        }

        _synth.SpeakAsync(pb);
    }

    public void SpeakScreen(LessonScreen screen)
    {
        if (screen?.Cards == null)
        {
            return;
        }

        _synth.SpeakAsyncCancelAll();
        var pb = new PromptBuilder();
        foreach (var card in screen.Cards)
        {
            Append(pb, card.En, "en-US");
            Append(pb, card.Ru, "ru-RU");
            pb.AppendBreak(PromptBreak.Medium);
        }

        _synth.SpeakAsync(pb);
    }

    public void Stop() => _synth.SpeakAsyncCancelAll();

    private static void Append(PromptBuilder pb, string text, string culture)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            pb.StartVoice(new CultureInfo(culture));
        }
        catch
        {
            // Voice for culture may be missing — still speak with default.
        }

        pb.AppendText(text.Replace('\n', ' '));
        try
        {
            pb.EndVoice();
        }
        catch
        {
            // ignored
        }

        pb.AppendBreak(PromptBreak.Small);
    }

    public void Dispose()
    {
        _synth.SpeakAsyncCancelAll();
        _synth.Dispose();
    }
}
