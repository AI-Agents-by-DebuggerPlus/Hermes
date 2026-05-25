using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>
/// Voice-oriented session announcements for Supabase <c>messages.content</c> (Android TTS).
/// Uses the same <see cref="BilingualSegmentFormatter"/> shape as ordinary chat rows.
/// </summary>
public static class HermesWpfSessionContextPayload
{
    private static readonly Regex SessionVoiceLinePattern = new(
        @"^(agent mode|trading mode|flashcards mode|assistant mode|режим агента|режим репетитора).*, project ",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ChatModeStatusLinePattern = new(
        @"^Проект:\s+.+\s·\sРежим:\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Plain phrase for TTS; language is chosen by <see cref="BilingualSegmentFormatter"/>.</summary>
    public static string BuildVoiceLine(string projectName, string modeId)
    {
        var project = string.IsNullOrWhiteSpace(projectName) ? "(no project)" : projectName.Trim();
        return modeId switch
        {
            HermesChatModeResolver.ModeTrading => $"trading mode, project {project}",
            HermesChatModeResolver.ModeAssistant => $"assistant mode, project {project}",
            HermesChatModeResolver.ModeEnglishTutor => $"режим репетитора английского, проект {project}",
            HermesChatModeResolver.ModeFlashcards => $"flashcards mode, project {project}",
            _ => $"agent mode, project {project}",
        };
    }

    /// <summary>e.g. <c>{"en":"trading mode, project TestTradingPlatform"}</c></summary>
    public static string BuildSupabaseContent(string projectName, string modeId) =>
        BilingualSegmentFormatter.ToSupabaseContent(BuildVoiceLine(projectName, modeId));

    public static bool IsSessionPayload(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (IsLegacySessionJson(content))
        {
            return true;
        }

        if (AppLifecycleSupabasePayload.IsStartupPayload(content))
        {
            return true;
        }

        var plain = BilingualSegmentFormatter.TryExtractVoicePlainText(content);
        if (plain is not null && ChatModeStatusLinePattern.IsMatch(plain.Trim()))
        {
            return true;
        }

        return plain is not null && IsSessionVoiceLine(plain);
    }

    private static bool IsLegacySessionJson(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content.Trim());
            return doc.RootElement.TryGetProperty("hermes_wpf", out var hp)
                   && string.Equals(hp.GetString(), "session", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSessionVoiceLine(string plain) =>
        SessionVoiceLinePattern.IsMatch(plain.Trim());
}
