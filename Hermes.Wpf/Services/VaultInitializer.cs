using System.IO;
using System.Text;

namespace Hermes.Wpf.Services;

/// <summary>Ensures role-oriented vault folder layout exists.</summary>
public static class VaultInitializer
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly (string Path, string Readme)[] Layout =
    [
        ("Identity", "User identity and profile notes."),
        ("Knowledge/Trading/Episodes", "Auto-captured trading episodes (PnL, risk, emergency stop)."),
        ("Knowledge/Trading", "Trading semantics, strategies, market notes."),
        ("Knowledge/Development", "Development and architecture knowledge."),
        ("Knowledge/English", "English tutor vocabulary and lessons."),
        ("Knowledge/Productivity", "Tasks, goals, habits."),
        ("Knowledge/Utilities", "Household utilities: Reni Water, ЖКХ automations."),
        ("Procedures/Utilities/ReniWater", "Reni vodokanal submit procedures and learning journal."),
        ("Knowledge/Hermes", "Platform documentation synced from Hermes.Wpf."),
        ("Procedures/Trading", "Trading procedures and playbooks."),
        ("Procedures/Dev", "Development workflows."),
        ("Procedures/English", "English tutor procedures."),
        ("Procedures/GeneratedSkills", "Exported generated skill metadata."),
        ("Projects", "Project-specific episodic memory."),
        // Biohacker role (см. hermes_biohacker_cursor_prompt_v2.md)
        ("Health", "Biohacker role: supplements, protocols, journal, schedule, goals, metrics."),
        ("Health/Supplements", "Cards for supplements / nootropics (one *.md per item)."),
        ("Health/Protocols", "Multi-step protocols (sleep, stress, energy, recovery)."),
        ("Health/Journal", "Daily health logs auto-captured by RoleExperienceCapture for the Biohacker role."),
        ("Health/Schedule", "Daily schedules (workday / weekend / optimized variants)."),
        ("Health/Goals", "Active health and cognitive goals."),
        ("Health/Metrics", "Aggregated metrics summaries (rolling 7/14/30 day windows)."),
    ];

    public static void EnsureLayout(string? vaultRoot)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot) || !Directory.Exists(vaultRoot))
        {
            return;
        }

        foreach (var (rel, readme) in Layout)
        {
            var dir = Path.Combine(vaultRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            var readmePath = Path.Combine(dir, "README.md");
            if (!File.Exists(readmePath))
            {
                File.WriteAllText(
                    readmePath,
                    $"# {Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar))}\n\n{readme}\n");
            }
        }

        EnsureBiohackerStartingFiles(vaultRoot);
    }

    private static void EnsureBiohackerStartingFiles(string vaultRoot)
    {
        // Health/Supplements/README.md — расширенная карточка-инструкция.
        TryWriteIfMissing(
            Path.Combine(vaultRoot, "Health", "Supplements", "README.md"),
            """
            ---
            type: reference
            role: Biohacker
            tags: [supplement, reference]
            importance: 3
            ---

            # Карточки БАДов и ноотропов

            Каждый файл в этой папке — карточка одного препарата.
            Hermes создаёт и обновляет карточки автоматически через {"bio":"update_supplement",...}.

            Поля: name, category, status, dose_mg, timing, frequency,
            stock_units, stock_days_left, reorder_threshold, observed_effects, stack_compatibility.
            """);

        TryWriteIfMissing(
            Path.Combine(vaultRoot, "Health", "Schedule", "README.md"),
            """
            ---
            type: reference
            role: Biohacker
            tags: [schedule, reference]
            importance: 3
            ---

            # Распорядок дня

            - workday.md — рабочий день
            - weekend.md — выходной день
            - optimized_*.md — варианты под конкретные цели

            Hermes обновляет расписание через {"bio":"update_schedule",...}.
            """);

        TryWriteIfMissing(
            Path.Combine(vaultRoot, "Health", "Goals", "cognitive_peak.md"),
            """
            ---
            type: health_goal
            role: Biohacker
            tags: [goal, health, cognitive, biohacking]
            goal_id: cognitive_peak
            title: Стабильная ясность ума и физическая энергия
            priority: 1
            status: active
            importance: 5
            ---

            # Цель: стабильная когнитивная ясность и энергия

            ## Метрики успеха
            - Фокус 8+/10 не менее 5 дней в неделю
            - Энергия при подъёме 7+/10 стабильно
            - Сон 7–8 ч с субъективным качеством 7+/10

            ## Активные вмешательства
            <!-- Hermes заполнит после первого разговора о здоровье -->

            ## Текущий статус
            <!-- Обновляется на основе Health/Journal/ -->
            """);
    }

    private static void TryWriteIfMissing(string path, string content)
    {
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"), Utf8NoBom);
    }
}
