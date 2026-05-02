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

    /// <summary>Polling interval while relay is enabled (seconds).</summary>
    public int SupabasePollIntervalSeconds { get; set; } = 3;

    public bool SupabaseUseAnonymousAuth { get; set; } = true;

    /// <summary>If true, first connect pulls the full remote transcript into the UI (no agent rerun).</summary>
    public bool SupabaseImportFullHistoryOnConnect { get; set; }

    /// <summary>Value stored in <c>sender_name</c> for rows published by Hermes.Wpf (mirror of DesktopVoiceChat convention).</summary>
    public string SupabaseHermesSenderName { get; set; } = "Hermes";

    /// <summary><c>sender_name</c> for rows inserted when the user sends from this desktop (must differ from <see cref="SupabaseHermesSenderName"/> and from mobile clients).</summary>
    public string SupabaseLocalSenderName { get; set; } = "Desktop";
}
