using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Builds reflective crystallization context from recent chat for skill_save generation.</summary>
public static class SkillReflectionService
{
    private const int MaxMessages = 12;
    private const int MaxCharsPerMessage = 1200;

    public static string BuildFromMessages(IEnumerable<ChatMessage> messages)
    {
        var list = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .TakeLast(MaxMessages)
            .ToList();

        if (list.Count == 0)
        {
            return "(нет недавних сообщений в чате)";
        }

        var sb = new StringBuilder();
        foreach (var m in list)
        {
            var role = (m.Role ?? "User").Trim();
            var text = Truncate(m.Text.Trim(), MaxCharsPerMessage);
            sb.Append('[').Append(role).Append("] ").AppendLine(text);
            sb.AppendLine("---");
        }

        return sb.ToString().TrimEnd();
    }

    public static string CrystallizeNowBlockRu(string reflectionContext) =>
        "### REFLECTIVE PHASE — crystallize skill NOW\n"
        + "Пользователь явно просит **сохранить решение как переиспользуемый навык**.\n"
        + "Проанализируй недавний диалог, выдели чистый переиспользуемый код/инструкцию и ответь **только** JSON "
        + "{\"skill\":\"skill_save\",…} (см. правила skill generation выше). Без Markdown, без пояснений.\n"
        + "Если в диалоге нет готового решения — верни skill_save с kind=prompt и outbound_prompt_block с процедурой.\n"
        + "\n--- Recent conversation (for reflection) ---\n"
        + reflectionContext.Trim()
        + "\n--- END conversation excerpt ---";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
