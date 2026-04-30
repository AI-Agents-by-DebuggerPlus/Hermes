namespace Hermes.Wpf.Models;

public sealed class HermesSettings
{
    public string WslDistro { get; set; } = "Ubuntu";
    public string VenvPath { get; set; } = "~/hermes-agent/venv";
    public string HermesCommand { get; set; } = "hermes";
    public int ChatTimeoutSeconds { get; set; } = 60;
    public bool AutoReconnect { get; set; } = true;
    public bool IsFirstRun { get; set; } = true;

    /// <summary>Windows paths of added projects (persisted across sessions).</summary>
    public List<string> SavedProjectPaths { get; set; } = [];

    /// <summary>Last folder opened in Browse (initial directory next time).</summary>
    public string? LastProjectBrowsePath { get; set; }

    /// <summary>Windows path of last selected project tab.</summary>
    public string? LastSelectedProjectPath { get; set; }
}
