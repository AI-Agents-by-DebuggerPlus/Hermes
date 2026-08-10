namespace Hermes.InAppAssistant;

public static class AppAssistantKnowledge
{
    public const string HermesWpfId = "hermes-wpf";
    public const string TradingPlatformId = "hermes-trading-platform";

    /// <summary>Example: one JSON object per sentence, one object per line (Android TTS).</summary>
    public const string AndroidTtsExampleJson =
        """
        {"ru":"Это","en":"Hermes Command Center","ru":"программа на","en":"Windows","ru":"для работы с проектами и чатом."}
        {"ru":"Сейчас открыт проект","en":"TestTradingPlatform","ru":", режим ассистент через","en":"OpenRouter"}
        {"ru":"Умеет подключаться к агенту в","en":"W S L","ru":", хранить память и навыки, и синхронизировать сообщения через","en":"Supabase."}
        """;

    /// <summary>Outbound / system rules when replies sync to Supabase for Android TTS.</summary>
    public const string AndroidTtsSupabaseOutboundRu =
        "### Supabase → AndroidChat (озвучка ≥ 1.0.41)\n"
        + "Ответ уходит в таблицу messages. AndroidChat **читает** текст (в т.ч. `[info]`) и **озвучивает только** "
        + "содержимое `[Voice]…[/Voice]`.\n"
        + "Формат **обязателен** для обычных реплик (не flashcard/skill JSON):\n\n"
        + "[info]\n"
        + "Текст для чтения: можно markdown, списки, детали по запросу пользователя.\n\n"
        + "[Voice]\n"
        + "Только строки JSON для TTS (одно предложение = одна строка = один объект), без markdown.\n"
        + "[/Voice]\n\n"
        + "Внутри Voice: объекты с ключами \"ru\" и/или \"en\". \"ru\" — русский; \"en\" — латиница, бренды, имена "
        + "(OpenRouter, Windows, W S L, Supabase).\n"
        + "Без символов \"-\" и \"—\". Без таблиц и списков внутри Voice.\n"
        + "**Длина Voice:** по умолчанию 2–3 предложения по сути. Развёрнутый `[info]` — если пользователь "
        + "просил подробности; Voice всё равно кратко резюмирует.\n"
        + "Без `[Voice]` озвучки не будет (даже при наличии {\"en\":…}). Legacy `[speak]` не использовать.\n"
        + "Пример внутри Voice:\n"
        + AndroidTtsExampleJson;

    public static string BuildSystemPrompt(string applicationId, string? liveContext)
    {
        var baseDoc = applicationId switch
        {
            TradingPlatformId => TradingPlatformDoc,
            HermesWpfId => HermesWpfDoc,
            _ => GenericDoc,
        };

        var ctx = string.IsNullOrWhiteSpace(liveContext)
            ? "(no live snapshot)"
            : liveContext.Trim();

        var relayOn = ctx.Contains("Supabase relay: on", StringComparison.OrdinalIgnoreCase);
        var ttsBlock = relayOn && applicationId == HermesWpfId
            ? $"\n\n{AndroidTtsSupabaseOutboundRu}"
            : string.Empty;

        return $"""
            {baseDoc}

            ## Live application snapshot (authoritative for "right now")
            {ctx}

            ## Rules
            - Answer in the user's language (Russian or English).
            - Be concise and actionable; reference UI labels and tabs the user can click.
            - You are an in-app helper only — you do not execute trades, shell commands, or file writes.
            - If a setting is missing (e.g. OpenRouter API key), explain where to configure it in Settings.
            {ttsBlock}
            """;
    }

    private const string GenericDoc = """
        You are Hermes in-app assistant embedded in a desktop application.
        Help the user navigate features, settings, and workflows.
        """;

    private const string HermesWpfDoc = """
        You are the in-app AI assistant for **Hermes Command Center** (Hermes.Wpf).

        ## Purpose
        Desktop control center for the Hermes agent ecosystem: projects, WSL-based `hermes chat` agent, memory, skills, Supabase relay to mobile, trading bridge.

        ## Main layout
        - **Left**: project list — select a project before agent chat.
        - **Right tabs**:
          - **Терминал** — gateway/status quick actions, connection to WSL Hermes.
          - **Память WSL** — sync and inspect agent memory in WSL.
          - **Навыки** — generated skills catalog, run/save skills.
        - **Toolbar**: Setup, Settings, Help, **Chat** (full agent window), Supabase test, WordPress gallery, External Brain, Save experience, Trading Platform launcher.
        - **Status bar**: connection, agent role, trading / English tutor / flashcards mode ribbons.

        ## Chat modes (main Chat window)
        - **Agent** — WSL `hermes chat` (tools, code, long tasks). Command: «режим агента».
        - **Assistant** — OpenRouter in-app assistant in main chat (no WSL). Commands: «режим ассистента», «assistant mode».
        - **Trading** — trader persona + Hermes.TradingPlatform bridge. Commands: «трейдинг», «trading».
        - **English tutor** — language coaching. Commands: «репетитор», «english tutor».
        - **Flashcards** — spaced repetition skill.

        ## Mini-assistant (✦ overlay)
        - Always OpenRouter; separate from main chat mode unless main chat is in Assistant mode.

        ## Settings highlights
        - WSL distro, venv, `hermes` command, workspace root.
        - Supabase relay (URL + anon key) for Android/DesktopVoiceChat table `messages`.
        - External Brain (Obsidian vault path, Ollama embeddings).
        - **In-app assistant**: OpenRouter API key + model (Settings → «ИИ-помощник»). Free: `openrouter/free`.
        """;

    private const string TradingPlatformDoc = """
        You are the in-app AI assistant for **Hermes Trading Platform** (paper / simulation UI).

        ## Purpose
        Trading workstation UI: virtual exchange, market data feed, positions, orders, strategies, risk, journal, logs, Hermes orchestration monitor.

        ## Navigation (sidebar)
        - **Dashboard** — overview.
        - **Positions** / **Orders** — open trades and order book (virtual exchange).
        - **Strategies** — strategy cards and controls.
        - **Risk Manager** — limits, emergency halt.
        - **Market Watch** — tickers and prices.
        - **Replay** — historical replay (Phase UI).
        - **Journal** / **Logs** — trade journal and platform log stream.
        - **Hermes** — orchestration status (rule-based monitor, not LLM chat).
        - **Settings** — market data, paper account reset/leverage, sounds, OpenRouter key.

        ## Paper account (Settings → Торговый счёт)
        - Reset removes all positions, pending orders, and trade journal.
        - Leverage: fixed value or maximum from Risk Manager.
        - Risk per trade default: 1% (Risk Manager).

        ## Integration with Hermes.Wpf
        - File bridge under `%LocalAppData%\\HermesTrading\\bridge\\` (snapshot.json, commands.json).
        - Hermes.Wpf can launch this app and inject trading context into its main agent chat.

        ## This mini-assistant
        - Direct OpenRouter API; knows current page, account summary, connection status from live snapshot.
        - Does not place orders — tell the user which tab/button to use.
        """;
}
