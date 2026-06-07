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
        + "**Навыки (generated skills) — CLI-first:**\n"
        + "1. **Подбор навыка:** Skill resolver ранжирует навыки из `~/.hermes/skills/` (TF-IDF; лог `[skill-resolver]`). "
        + "kind=intent → JSON `{\"skill\":\"wpf_local\",\"action\":\"…\"}`; kind=script → `run_generated`; kind=prompt → outbound_prompt_block.\n"
        + "2. **Windows tools внутри skills:** Playwright/Reni Water и др. — через wpf_local **после** твоего ответа, не pre-CLI intercept.\n"
        + "3. **Post-local hook:** после wpf_local клиент отправит structured результат обратно в CLI для памяти и skill_save.\n"
        + "4. **Создание навыка:** `{\"skill\":\"skill_save\",…}` → sandbox → `%AppData%\\HermesWpf\\skills\\` + зеркало `~/.hermes/skills/`.\n"
        + "5. **НЕ автоматически:** новый навык без `skill_save` / явной просьбы.\n\n"
        + "**Ответы пользователю:** если спрашивают «как работает память/навыки/самообучение в Hermes» — опирайся на этот блок и при необходимости на vault-заметку `Knowledge/Hermes/Experience_and_Skills_Logic.md`. Не утверждай, что функции из старых README уже есть в WPF, если их нет в списке выше.\n"
        + "Подробный отчёт в репозитории: `Docs/Report/Experience_And_Skills_Logic_Report.md`.\n\n"
        + "**Hermes Binance Demo Spot Terminal:** `Hermes.BinanceDemoSpotTerminal.exe` (Spot Demo, demo-api.binance.com).\n"
        + "**Hermes Binance Demo Futures Terminal:** `Hermes.BinanceDemoFuturesTerminal.exe` (USDT-M Futures Demo, demo-fapi.binance.com). "
        + "В режиме трейдинга Hermes читает snapshot (балансы, позиции, ордера) и исполняет JSON-команды `{\"skill\":\"trading\",\"market\":\"futures\",...}` через file-bridge.\n"
        + "Legacy `Hermes.SpotTerminal` и `Hermes.TradingPlatform.exe` в основном решении отключены.";

    /// <summary>Vault note body (without YAML) — synced when repo report is unavailable.</summary>
    public static string VaultMarkdownBody =>
        "# Hermes.Wpf: память, самообучение и навыки\n\n"
        + "Автосинхронизация из Hermes.Wpf. Полная версия: `Docs/Report/Experience_And_Skills_Logic_Report.md`.\n\n"
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
