using System.Globalization;
using System.Linq;
using System.Text;

namespace Hermes.Wpf.Services;

/// <summary>One-shot Hermes prompts for autonomous flashcard rows (posted to Supabase).</summary>
public static class FlashcardHermesGenerationPrompt
{
    public static string BuildUserPayload(string topic, IReadOnlyList<string> alreadySentEnglish, bool retryStricterPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[flashcards / machine-only]");
        sb.AppendLine("Reply with ONE JSON object only, no Markdown, no prose before or after.");
        sb.AppendLine("{\"type\":\"flashcard\",\"en\":\"<word or short English phrase ≤5 words>\",\"ru\":\"<natural Russian translation>\"}");
        sb.AppendLine("Rules: vary difficulty within topic; enforce unique English side for this session;");
        sb.Append(CultureInfo.InvariantCulture, $"Topic: \"{EscapeJsonString(topic)}\".");

        if (alreadySentEnglish.Count > 0)
        {
            sb.AppendLine();
            sb.Append("Already sent in this session (do not reuse these English strings exactly): ");
            sb.Append(string.Join(", ", alreadySentEnglish.Select(static e => '\"' + EscapeJsonString(e) + '\"')));
            sb.Append('.');
        }

        if (retryStricterPrompt)
        {
            sb.AppendLine();
            sb.AppendLine("Previous reply was rejected: output strict JSON matching the schema, ASCII quotes only.");
        }

        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <remarks>Minimal escape for quoting in natural-language Hermes payloads (not RFC JSON encoder).</remarks>
    private static string EscapeJsonString(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
