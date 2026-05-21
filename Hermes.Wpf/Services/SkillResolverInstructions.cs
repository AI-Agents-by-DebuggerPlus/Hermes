using System.Globalization;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Prompt blocks so Hermes picks the right saved skill for the current task.</summary>
public static class SkillResolverInstructions
{
    public static string TaskMatchBlockRu(IReadOnlyList<SkillTaskMatch> matches)
    {
        if (matches.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("### Skill resolver — Hermes.Wpf matched saved skills for THIS user task");
        sb.AppendLine(
            "Перед ответом **сам определи**, нужен ли сохранённый навык. Если навык подходит — **используй его**, "
            + "не переписывай с нуля то, что уже есть в каталоге.");
        sb.AppendLine();
        sb.AppendLine("Правила выбора:");
        sb.AppendLine(
            "1. Если лучший навык с score ≥ 0.5 и kind=script|intent — в начале ответа выведи **только** JSON "
            + "{\"skill\":\"run_generated\",\"id\":\"<id>\"} (без Markdown), затем краткий комментарий пользователю.");
        sb.AppendLine(
            "2. Если kind=prompt — следуй outbound_prompt_block навыка, не дублируй инструкции.");
        sb.AppendLine("3. Если ни один навык не подходит — отвечай обычно, без JSON run_generated.");
        sb.AppendLine();
        sb.AppendLine("Ранжирование клиента:");

        var i = 1;
        foreach (var m in matches)
        {
            var s = m.Skill;
            sb.Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(". **")
                .Append(s.Id)
                .Append("** (score=")
                .Append(m.Score.ToString("F2", CultureInfo.InvariantCulture))
                .Append(", ")
                .Append(s.Kind)
                .Append(", ")
                .Append(m.Reason)
                .AppendLine(")");
            if (!string.IsNullOrWhiteSpace(s.Summary))
            {
                sb.AppendLine("   " + s.Summary.Trim());
            }

            if (s.Triggers.Count > 0)
            {
                sb.Append("   triggers: ")
                    .AppendLine(string.Join(", ", s.Triggers.Take(6)));
            }

            sb.AppendLine();
            i++;
        }

        return sb.ToString().TrimEnd();
    }
}
