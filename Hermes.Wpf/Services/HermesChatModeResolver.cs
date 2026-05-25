using Hermes.Wpf.Models;
using Hermes.Wpf.Skills;

namespace Hermes.Wpf.Services;

/// <summary>Current Hermes.Wpf persona for UI and Supabase session context.</summary>
public static class HermesChatModeResolver
{
    public const string ModeAgent = "agent";
    public const string ModeAssistant = "assistant";
    public const string ModeTrading = "trading";
    public const string ModeEnglishTutor = "english_tutor";
    public const string ModeFlashcards = "flashcards";

    public static string ResolveModeId(HermesSettings? settings, FlashcardStatus flashcardStatus)
    {
        if (settings is null)
        {
            return ModeAgent;
        }

        if (flashcardStatus != FlashcardStatus.Idle)
        {
            return ModeFlashcards;
        }

        if (settings.AssistantModeEnabled)
        {
            return ModeAssistant;
        }

        if (settings.TradingModeEnabled)
        {
            return ModeTrading;
        }

        if (settings.EnglishTutorModeEnabled)
        {
            return ModeEnglishTutor;
        }

        return ModeAgent;
    }

    public static string ResolveModeDisplayRu(string modeId) =>
        modeId switch
        {
            ModeTrading => "трейдинг",
            ModeAssistant => "ассистент",
            ModeEnglishTutor => "репетитор EN",
            ModeFlashcards => "карточки",
            _ => "агент",
        };

    public static string BuildChatStatusLine(string? projectName, HermesSettings? settings, FlashcardStatus flashcardStatus)
    {
        var project = string.IsNullOrWhiteSpace(projectName) ? "—" : projectName.Trim();
        var modeId = ResolveModeId(settings, flashcardStatus);
        var mode = ResolveModeDisplayRu(modeId);
        return $"Проект: {project} · Режим: {mode}";
    }
}
