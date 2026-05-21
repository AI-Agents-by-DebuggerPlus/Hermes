namespace Hermes.Wpf.Services;

/// <summary>
/// Built-in knowledge for Hermes about Hermes.Wpf memory, self-learning, and generated skills.
/// Injected into every outbound <c>hermes chat</c> payload (not shown in the chat bubble).
/// </summary>
public static class HermesPlatformKnowledgeInstructions
{
    /// <summary>Compact operational summary — always appended to outbound prompts.</summary>
    public const string OutboundBlockRu =
        "### Платформа Hermes.Wpf: память, самообучение и навыки (актуальная реализация)\n"
        + "Ты работаешь через клиент **Hermes.Wpf** (Windows) → WSL **hermes chat**. Ниже — как устроено **в этой сборке**; не выдумывай Voyager/Docker/Chroma, если пользователь не просит «как в теории».\n\n"
        + "**Накопление опыта (memory):**\n"
        + "- **Сессия:** история чата по проекту на диске.\n"
        + "- **Долгосрочно:** External Brain — Markdown vault (`*.md`), при включённом inject релевантные фрагменты подмешиваются в этот промпт (lexical или vector/Ollama, лог `[vector-memory]`).\n"
        + "- **После каждого успешного ответа Hermes:** клиент строит черновик `MemoryDraft` (`MemoryExtractorService`); **в vault автоматически не пишется** — пользователь сохраняет вручную (Memory Editor) в `Knowledge/`, `Procedures/`, `Projects/`, `Identity/`.\n"
        + "- **WSL:** `~/.hermes/memories/USER.md`, `MEMORY.md` → экспорт в vault (`Identity/`, `Knowledge/`), если включён sync в Settings.\n"
        + "- **Не реализовано:** автосохранение каждого черновика; SOUL.md self-edit; отдельная векторная БД вне vault.\n\n"
        + "**Навыки (generated skills) — два режима:**\n"
        + "1. **Автоматически (только подбор существующих):** перед `hermes chat` Skill resolver ранжирует сохранённые навыки по задаче (TF-IDF + ключевые слова; лог `[skill-resolver]`). Если score высокий — в промпт попадает блок «matched skills»; ты должен предпочесть `{\"skill\":\"run_generated\",\"id\":\"…\"}` вместо переписывания с нуля. Локально клиент может запустить навык по триггеру или фразе «запусти навык &lt;id&gt;».\n"
        + "2. **По запросу (создание нового):** только когда пользователь просит **«сохрани как навык»** / кристаллизацию, или ты возвращаешь JSON `{\"skill\":\"skill_save\",…}` → клиент: sandbox (`[skill-sandbox]`) → `%AppData%\\HermesWpf\\skills\\&lt;id&gt;\\` (manifest.json, SKILL.md, run.ps1|py), зеркало `~/.hermes/skills/`, `index.json`, заметка в vault `Procedures/GeneratedSkills/`.\n"
        + "3. **НЕ автоматически:** новый навык **не** создаётся после каждой успешной задачи без `skill_save` / явной просьбы.\n\n"
        + "**Ответы пользователю:** если спрашивают «как работает память/навыки/самообучение в Hermes» — опирайся на этот блок и при необходимости на vault-заметку `Knowledge/Hermes/Experience_and_Skills_Logic.md`. Не утверждай, что функции из старых README уже есть в WPF, если их нет в списке выше.\n"
        + "Подробный отчёт в репозитории: `Docs/Experience_And_Skills_Logic_Report.md`.\n\n"
        + "**Hermes Trading Platform (paper terminal):** отдельное приложение `Hermes.TradingPlatform.exe`. При включённой интеграции в Settings клиент подмешивает live snapshot (позиции, баланс, ордера, риск, стратегии) и принимает JSON `{\"skill\":\"trading\",…}` для ордеров и алгоритмов через file-bridge + CLI. Ордера проходят virtual exchange и RiskValidator. Подробнее: `Docs/Hermes_Trading_Platform_Integration.md`.";

    /// <summary>Vault note body (without YAML) — synced when repo report is unavailable.</summary>
    public static string VaultMarkdownBody =>
        "# Hermes.Wpf: память, самообучение и навыки\n\n"
        + "Автосинхронизация из Hermes.Wpf. Полная версия: `Docs/Experience_And_Skills_Logic_Report.md`.\n\n"
        + "## Память\n"
        + "- External Brain vault, vector/lexical retrieval в промпт.\n"
        + "- Черновик опыта после каждого ответа; ручное сохранение в vault.\n"
        + "- WSL USER.md / MEMORY.md → vault.\n\n"
        + "## Навыки\n"
        + "- **Авто:** skill resolver — подбор существующего навыка под задачу.\n"
        + "- **По запросу:** skill_save + sandbox → папка skills.\n"
        + "- **Не авто:** создание навыка после каждой задачи без запроса.\n\n"
        + "## Теги для поиска\n"
        + "#hermes #memory #skills #external-brain #skill-resolver #skill-save #самообучение #навыки #опыт\n";

    public const string VaultRelativePath = "Knowledge/Hermes/Experience_and_Skills_Logic.md";
    public const string VaultFileName = "Experience_and_Skills_Logic.md";
}
