namespace Hermes.Wpf.Models;

public sealed class HermesSettings
{
    public string WslDistro { get; set; } = "Ubuntu";
    public string VenvPath { get; set; } = "~/hermes-agent/venv";
    public string HermesCommand { get; set; } = "hermes";
    /// <summary>Hermes chat can run long (tools, model); 60s was too tight for real use.</summary>
    public int ChatTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// When true, append a short system note to outbound <c>hermes chat</c> payloads only (not shown in UI / chat file)
    /// so the model avoids claiming it “sees the screen” without a successful capture.
    /// </summary>
    public bool AppendVisionScopeReminder { get; set; } = true;

    /// <summary>Custom reminder text when <see cref="AppendVisionScopeReminder"/> is true; empty = built-in Russian text.</summary>
    public string VisionScopeReminderNote { get; set; } = string.Empty;
    public bool AutoReconnect { get; set; } = true;
    public bool IsFirstRun { get; set; } = true;

    /// <summary>When true, log sanitized bash -lc scripts for Hermes invocations (also always in DEBUG builds).</summary>
    public bool DiagnosticLogHermesCommands { get; set; }

    /// <summary>Windows paths of added projects (persisted across sessions).</summary>
    public List<string> SavedProjectPaths { get; set; } = [];

    /// <summary>Last folder opened in Browse (initial directory next time).</summary>
    public string? LastProjectBrowsePath { get; set; }

    /// <summary>Windows path of last selected project tab.</summary>
    public string? LastSelectedProjectPath { get; set; }

    /// <summary>Chat pane font size in WPF dips (labeled “pt” in Settings).</summary>
    public double ChatFontSize { get; set; } = 14;

    /// <summary>
    /// When true, local chat sends and inbound Supabase user rows do not invoke Hermes; messages still appear in UI and outgoing desktop messages still publish to Supabase when relay is on.
    /// </summary>
    public bool HermesAgentPaused { get; set; }

    /// <summary>
    /// When set and directory exists, Hermes subprocess <c>cd</c> uses this folder (full tree under it is reachable).
    /// When empty, each command uses the selected project folder as working directory (previous behavior).
    /// </summary>
    public string WorkspaceRootWindowsPath { get; set; } = string.Empty;

    /// <summary>Remember last folder picker for workspace root browse.</summary>
    public string? LastWorkspaceBrowsePath { get; set; }

    /// <summary>Mirror chat with DesktopVoiceChat / Android: Supabase Postgres table <c>messages</c>.</summary>
    public bool SupabaseRelayEnabled { get; set; }

    public string SupabaseUrl { get; set; } = string.Empty;
    public string SupabaseAnonKey { get; set; } = string.Empty;

    /// <summary>
    /// If true, send <c>created_at</c> as local system time (with offset) rather than UTC.
    /// Use when the server/table expects client-side timestamps similar to DesktopVoiceChat.
    /// </summary>
    public bool SupabaseUseLocalCreatedAt { get; set; }

    /// <summary>Polling interval while relay is enabled (seconds).</summary>
    public int SupabasePollIntervalSeconds { get; set; } = 3;

    public bool SupabaseUseAnonymousAuth { get; set; } = true;

    /// <summary>If true, first connect pulls the full remote transcript into the UI (no agent rerun).</summary>
    public bool SupabaseImportFullHistoryOnConnect { get; set; }

    /// <summary>Value stored in <c>sender_name</c> for rows published by Hermes.Wpf (mirror of DesktopVoiceChat convention).</summary>
    public string SupabaseHermesSenderName { get; set; } = "Hermes";

    /// <summary><c>sender_name</c> for rows inserted when the user sends from this desktop (must differ from <see cref="SupabaseHermesSenderName"/> and from mobile clients).</summary>
    public string SupabaseLocalSenderName { get; set; } = "Desktop";

    /// <summary>When true, only rows with <c>recipient_name</c> = <see cref="SupabaseInboundRecipientName"/> are injected into chat / trigger Hermes.</summary>
    public bool SupabaseFilterInboundByRecipient { get; set; } = true;

    /// <summary>Required <c>recipient_name</c> for inbound Supabase rows (default Hermes).</summary>
    public string SupabaseInboundRecipientName { get; set; } = "Hermes";

    /// <summary><c>recipient_name</c> for rows published by Hermes agent from this desktop (default Android).</summary>
    public string SupabaseHermesOutboundRecipientName { get; set; } = "Android";

    /// <summary><c>recipient_name</c> for user messages sent from this desktop into Supabase (default Hermes).</summary>
    public string SupabaseLocalOutboundRecipientName { get; set; } = "Hermes";

    /// <summary>Monitor WhatsApp Web (WebView2) and inject new messages from <see cref="WhatsAppContactDisplayName"/> into chat.</summary>
    public bool WhatsAppWebEnabled { get; set; } = true;

    /// <summary>Contact to open in WhatsApp Web (matches «My Fido (You)» when set to My Fido).</summary>
    public string WhatsAppContactDisplayName { get; set; } = "My Fido";

    /// <summary>DOM poll interval for WhatsApp Web monitor (ms, min 500).</summary>
    public int WhatsAppPollIntervalMs { get; set; } = 2000;

    /// <summary>When false, all new messages after chat open are forwarded (no prefix filter).</summary>
    public bool WhatsAppTextMarkerEnabled { get; set; } = true;

    /// <summary>Prefix filter when <see cref="WhatsAppTextMarkerEnabled"/> is true (default [gemini]).</summary>
    public string WhatsAppTextMarker { get; set; } = "[gemini]";

    /// <summary>Effective prefix passed to the WhatsApp monitor (empty = disabled).</summary>
    public string GetEffectiveWhatsAppTextMarker() =>
        !WhatsAppTextMarkerEnabled
            ? string.Empty
            : string.IsNullOrWhiteSpace(WhatsAppTextMarker) ? "[gemini]" : WhatsAppTextMarker.Trim();

    /// <summary>When true, new WhatsApp messages invoke Hermes agent (like Supabase inbound).</summary>
    public bool WhatsAppTriggerHermesAgent { get; set; } = true;

    /// <summary>After baseline, send a probe message in WhatsApp Web and wait until DOM poll detects it.</summary>
    public bool WhatsAppParseProbeEnabled { get; set; } = true;

    /// <summary>When true, forward WhatsApp messages of 1 character (default minimum is 2).</summary>
    public bool WhatsAppAllowSingleCharMessages { get; set; }

    public int GetEffectiveWhatsAppMinTextLength() => WhatsAppAllowSingleCharMessages ? 1 : 2;

    /// <summary>Windows path to Obsidian vault / Markdown memory root (recursive <c>*.md</c>).</summary>
    public string ExternalBrainMemoryPath { get; set; } = string.Empty;

    /// <summary>Merge relevant memories into outbound <c>hermes chat</c> prompt (not shown in UI bubble).</summary>
    public bool ExternalBrainInjectIntoPrompt { get; set; } = true;

    /// <summary>Maximum memories appended to the outbound prompt context block (clamped 1–20).</summary>
    public int ExternalBrainMaxContextItems { get; set; } = 12;

    /// <summary>Semantic retrieval (TF-IDF / Ollama embeddings) instead of token overlap only.</summary>
    public bool ExternalBrainVectorRetrievalEnabled { get; set; } = true;

    /// <summary>When true and Ollama is reachable, use dense embeddings; otherwise TF-IDF.</summary>
    public bool ExternalBrainUseOllamaEmbeddings { get; set; } = true;

    /// <summary>Ollama base URL for /api/embed (e.g. http://127.0.0.1:11434).</summary>
    public string ExternalBrainOllamaBaseUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>Ollama embedding model (nomic-embed-text, all-minilm, …).</summary>
    public string ExternalBrainEmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Export <c>~/.hermes/memories/*.md</c> from WSL into the External Brain vault.</summary>
    public bool SyncWslAgentMemoryToExternalBrain { get; set; } = true;

    /// <summary>Allow Hermes to crystallize reusable skills into the local skills catalog.</summary>
    public bool SkillGenerationEnabled { get; set; } = true;

    /// <summary>Mirror saved skills to WSL <c>~/.hermes/skills/</c>.</summary>
    public bool SkillMirrorToWslHermes { get; set; }

    /// <summary>Windows root for generated skills; empty = %AppData%\\HermesWpf\\skills.</summary>
    public string GeneratedSkillsDirectory { get; set; } = string.Empty;

    /// <summary>Max folder suffix attempts when skill id already exists (1–10).</summary>
    public int SkillMaxGenerationAttempts { get; set; } = 3;

    /// <summary>Run optional test_command from skill_save JSON before confirming save.</summary>
    public bool SkillRunTestsBeforeSave { get; set; } = true;

    /// <summary>Execute script skills in temp sandbox before persisting to skills folder.</summary>
    public bool SkillSandboxBeforeSave { get; set; } = true;

    /// <summary>Sandbox / script run timeout (seconds, 5–300).</summary>
    public int SkillSandboxTimeoutSeconds { get; set; } = 60;

    /// <summary>Rank saved skills against each user task and inject resolver block into Hermes prompt.</summary>
    public bool SkillAutoResolveForTasks { get; set; } = true;

    /// <summary>Max skill candidates in resolver block (1–8).</summary>
    public int SkillResolveMaxSuggestions { get; set; } = 3;

    /// <summary>Minimum match score 0.1–0.9 for resolver suggestions.</summary>
    public double SkillResolveMinScore { get; set; } = 0.28;

    /// <summary>
    /// When true, Hermes.Wpf may run synthetic cursor moves/tests (desktop skill).
    /// The standalone <c>Hermes.MouseBridge</c> CLI does not read this flag.
    /// </summary>
    public bool DesktopMouseSkillEnabled { get; set; }

    /// <summary>Monitor index for desktop screenshots (0-based). -1 = primary display.</summary>
    public int DesktopScreenshotMonitorIndex { get; set; } = -1;

    /// <summary>
    /// Optional duplicate copy folder. Primary files always go to
    /// %LocalAppData%\HermesWpf\screenshots. Empty = no duplicate.
    /// </summary>
    public string DesktopScreenshotDirectory { get; set; } = string.Empty;

    /// <summary>After monitor capture, invoke Hermes CLI with vision_analyze on the screenshot files.</summary>
    public bool DesktopVisionAnalyzeEnabled { get; set; } = true;

    /// <summary>When true, vision_analyze uses the annotated regions image; otherwise the plain PNG.</summary>
    public bool DesktopVisionUseAnnotatedImage { get; set; } = true;

    /// <summary>After capture, send plain PNG to WordPress hermes-image-receiver gallery.</summary>
    public bool HermesGalleryPublishEnabled { get; set; } = true;

    /// <summary>WordPress site base URL, e.g. https://example.com</summary>
    public string HermesGallerySiteUrl { get; set; } = string.Empty;

    /// <summary>Legacy: full REST URL; use <see cref="HermesGallerySiteUrl"/> instead.</summary>
    public string HermesGalleryRestUrl { get; set; } = string.Empty;

    /// <summary>Upload retry count for REST (hermes-image-receiver).</summary>
    public int HermesGalleryMaxRetries { get; set; } = 3;

    /// <summary>WebSocket URL, e.g. ws://site.com:8765 (optional; needs server proxy).</summary>
    public string HermesGalleryWebSocketUrl { get; set; } = string.Empty;

    /// <summary>Secret token from WP → Settings → Hermes Receiver.</summary>
    public string HermesGalleryToken { get; set; } = string.Empty;

    /// <summary>Sender/channel for POST /message and shortcode [hermes_gallery channel="…"]. Empty/default → machine name.</summary>
    public string HermesGalleryChannel { get; set; } = "";

    /// <summary>When true and WebSocket URL is set, try WebSocket first then REST on failure.</summary>
    public bool HermesGalleryPreferWebSocket { get; set; }

    // Legacy (migrated on load from settings.json)
    public bool WordPressScreenshotPublishEnabled { get; set; }
    public string WordPressSiteUrl { get; set; } = string.Empty;
    public string WordPressScreenshotApiKey { get; set; } = string.Empty;

    /// <summary>When true, outbound chat payloads include English tutor persona (Hermes.Wpf).</summary>
    public bool EnglishTutorModeEnabled { get; set; }

    /// <summary>When true, Hermes acts as trader-executor for Hermes Trading Platform (toggle: трейдинг / trading).</summary>
    public bool TradingModeEnabled { get; set; }

    /// <summary>When true, main chat uses OpenRouter in-app assistant instead of WSL hermes.</summary>
    public bool AssistantModeEnabled { get; set; }

    /// <summary>Folder with <c>run_submit.ps1</c> (Reni vodokanal). Empty = auto-detect from workspace / repo.</summary>
    public string ReniWaterScriptDirectory { get; set; } = @"D:\Programming\AI_Agents\Hermes\scripts\reni_water";

    /// <summary>Written by Python after submit; hourly notify until ack.</summary>
    public string ReniWaterPendingAckPath { get; set; } = @"d:\Documents\Utilities\water\pending_ack.json";

    /// <summary>How often Hermes.Wpf refreshes the pending-ack status bar (minutes).</summary>
    public int ReniWaterPendingPollMinutes { get; set; } = 15;

    /// <summary><c>once</c> or <c>monthly</c>; empty = no in-app schedule.</summary>
    public string ReniWaterScheduleKind { get; set; } = string.Empty;

    /// <summary>ISO local time for one-shot schedule.</summary>
    public string? ReniWaterNextRunLocal { get; set; }

    /// <summary>Monthly window start (default 1).</summary>
    public int ReniWaterMonthlyWindowStartDay { get; set; } = 1;

    /// <summary>Monthly window end (default 5).</summary>
    public int ReniWaterMonthlyWindowEndDay { get; set; } = 5;

    public int ReniWaterScheduleHour { get; set; } = 9;

    public int ReniWaterScheduleMinute { get; set; }

    /// <summary>Legacy; migrated to <see cref="ReniWaterMonthlyWindowStartDay"/> on load.</summary>
    public int ReniWaterMonthlyDay { get; set; } = 1;

    /// <summary><c>yyyy-MM</c> — last month when monthly job ran.</summary>
    public string? ReniWaterLastMonthlyRunKey { get; set; }

    /// <summary>Run memory extraction + vault write after successful built-in local handlers.</summary>
    public bool LocalLearningLoopEnabled { get; set; } = true;

    /// <summary>After wpf_local execution, send structured result back to Hermes CLI for memory/reflection.</summary>
    public bool CliPostLocalFollowUpEnabled { get; set; } = true;

    /// <summary>Auto-create/update generated skill mirror for Reni Water after N successful submits.</summary>
    public bool ReniWaterAutoCrystallizeEnabled { get; set; }

    /// <summary>Successful submit count before auto skill crystallization (default 2).</summary>
    public int ReniWaterAutoCrystallizeAfterSuccesses { get; set; } = 2;

    /// <summary>Persisted successful Reni submit count for auto-crystallize threshold.</summary>
    public int ReniWaterLearningSuccessCount { get; set; }

    /// <summary>Inject Trading Platform bridge instructions and live snapshot into outbound hermes chat.</summary>
    public bool TradingPlatformIntegrationEnabled { get; set; } = false;

    /// <summary>Start Hermes.TradingPlatform.exe when bridge heartbeat is stale (if exe path resolves).</summary>
    public bool TradingPlatformAutoLaunchTerminal { get; set; } = false;

    /// <summary>Optional full path to Hermes.TradingPlatform.Cli.exe; empty = auto-detect next to Hermes.Wpf or dev tree.</summary>
    public string TradingPlatformCliPath { get; set; } = string.Empty;

    /// <summary>Optional full path to Hermes.TradingPlatform.exe for auto-launch.</summary>
    public string TradingPlatformExePath { get; set; } = string.Empty;

    /// <summary>Inject Spot Terminal bridge (agent, spot balances, skills) into Hermes chat.</summary>
    public bool SpotTerminalIntegrationEnabled { get; set; } = false;

    /// <summary>Auto-launch Hermes.SpotTerminal.exe when spot heartbeat is stale.</summary>
    public bool SpotTerminalAutoLaunch { get; set; } = false;

    public string SpotTerminalExePath { get; set; } = string.Empty;

    public string SpotTerminalCliPath { get; set; } = string.Empty;

    /// <summary>Inject Binance Demo Futures bridge into Hermes chat.</summary>
    public bool FuturesTerminalIntegrationEnabled { get; set; } = true;

    /// <summary>Auto-launch Hermes.BinanceDemoFuturesTerminal.exe when futures heartbeat is stale.</summary>
    public bool FuturesTerminalAutoLaunch { get; set; } = true;

    public string FuturesTerminalExePath { get; set; } = string.Empty;

    /// <summary>Persisted active agent role (enum name).</summary>
    public string PersistedAgentRole { get; set; } = nameof(AgentRole.Universal);

    /// <summary>Inject ROLE CONTEXT block into outbound hermes chat.</summary>
    public bool RoleContextBlockEnabled { get; set; } = true;

    /// <summary>Auto-save high-importance MemoryDraft to role Knowledge folder.</summary>
    public bool RoleAutoCapture { get; set; } = true;

    public int RoleAutoCaptureMinImportance { get; set; } = 4;

    public int RoleAutoCaptureMinLength { get; set; } = 150;

    /// <summary>Enable Biohacker role end-to-end (state service, intent handler, persona prompt).</summary>
    public bool BiohackerEnabled { get; set; } = true;

    /// <summary>Run SupplementStockTracker.RunDailyCheckIfNeededAsync at application startup.</summary>
    public bool BiohackerStockCheckOnStartup { get; set; } = true;

    /// <summary>Plain-text trading safety rules injected into trading-mode agent prompt (stricter-wins over terminal risk manager).</summary>
    public string TradingSafetyRulesText { get; set; } =
        "Маржа на сделку — не более 1% депозита (даже если в риск-менеджере терминала больше).\n"
        + "Максимальный убыток за день — 50 USDT. После лимита — только close_position, без новых входов.\n"
        + "Только BTCUSDT и ETHUSDT для открытия новых позиций.";

    /// <summary>Export significant trading events to vault Knowledge/Trading/Episodes.</summary>
    public bool TradingExperienceExportEnabled { get; set; } = true;

    public double TradingExperiencePnlThreshold { get; set; } = 50.0;

    /// <summary>Fractional drawdown from peak equity (0.05 = 5%).</summary>
    public double TradingExperienceDrawdownThreshold { get; set; } = 0.05;

    /// <summary>OpenRouter API key for in-app assistant (sk-or-v1-…).</summary>
    public string InAppAssistantOpenRouterApiKey { get; set; } = string.Empty;

    /// <summary>Model id for in-app assistant (e.g. openrouter/free or meta-llama/llama-3.2-3b-instruct:free).</summary>
    public string InAppAssistantOpenRouterModel { get; set; } = "openrouter/free";
}
