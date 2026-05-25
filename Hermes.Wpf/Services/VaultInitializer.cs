using System.IO;

namespace Hermes.Wpf.Services;

/// <summary>Ensures role-oriented vault folder layout exists.</summary>
public static class VaultInitializer
{
    private static readonly (string Path, string Readme)[] Layout =
    [
        ("Identity", "User identity and profile notes."),
        ("Knowledge/Trading/Episodes", "Auto-captured trading episodes (PnL, risk, emergency stop)."),
        ("Knowledge/Trading", "Trading semantics, strategies, market notes."),
        ("Knowledge/Development", "Development and architecture knowledge."),
        ("Knowledge/English", "English tutor vocabulary and lessons."),
        ("Knowledge/Productivity", "Tasks, goals, habits."),
        ("Knowledge/Hermes", "Platform documentation synced from Hermes.Wpf."),
        ("Procedures/Trading", "Trading procedures and playbooks."),
        ("Procedures/Dev", "Development workflows."),
        ("Procedures/English", "English tutor procedures."),
        ("Procedures/GeneratedSkills", "Exported generated skill metadata."),
        ("Projects", "Project-specific episodic memory."),
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
    }
}
