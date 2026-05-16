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

    /// <summary>Windows path to Obsidian vault / Markdown memory root (recursive <c>*.md</c>).</summary>
    public string ExternalBrainMemoryPath { get; set; } = string.Empty;

    /// <summary>Merge relevant memories into outbound <c>hermes chat</c> prompt (not shown in UI bubble).</summary>
    public bool ExternalBrainInjectIntoPrompt { get; set; } = true;

    /// <summary>Maximum memories appended to the outbound prompt context block (clamped 1–20).</summary>
    public int ExternalBrainMaxContextItems { get; set; } = 12;

    /// <summary>Export <c>~/.hermes/memories/*.md</c> from WSL into the External Brain vault.</summary>
    public bool SyncWslAgentMemoryToExternalBrain { get; set; } = true;

    /// <summary>
    /// When true, Hermes.Wpf may run synthetic cursor moves/tests (desktop skill).
    /// The standalone <c>Hermes.MouseBridge</c> CLI does not read this flag.
    /// </summary>
    public bool DesktopMouseSkillEnabled { get; set; }

    /// <summary>When true, outbound chat payloads include English tutor persona (Hermes.Wpf).</summary>
    public bool EnglishTutorModeEnabled { get; set; }

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
}
