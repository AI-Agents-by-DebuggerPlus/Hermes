namespace Hermes.RemoteTerminal.Models;

public sealed class AppSettings
{
    public string SupabaseUrl { get; set; } = "https://dauvhkttddxmqfkfunqg.supabase.co";
    public string SupabaseAnonKey { get; set; } = string.Empty;
    public string RecipientName { get; set; } = "RemoteTerminal";
    public string LocalSenderName { get; set; } = "RemoteTerminal";
    public int MaxLines { get; set; } = 800;
    public bool ShowAllRecipients { get; set; }
    public bool MirrorLocalLogsToSupabase { get; set; }

    /// <summary>
    /// When true and WebSocket is offline — REST poll backup (same channel as RemoteTerminal.Xp).
    /// Not a silent auto-fallback: must be explicitly enabled in settings.
    /// </summary>
    public bool RestBackupBridgeEnabled { get; set; }

    /// <summary>REST backup poll interval (seconds).</summary>
    public int RestBackupPollSeconds { get; set; } = 15;

    /// <summary>Fonts, colors, scheme — saved next to EXE.</summary>
    public UiThemeSettings Ui { get; set; } = UiThemeSettings.FromScheme("DarkBinance");
}

public sealed class TerminalLine
{
    public required string Sender { get; init; }
    public required string Recipient { get; init; }
    public required string Content { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
    public Guid? MessageId { get; init; }
}
