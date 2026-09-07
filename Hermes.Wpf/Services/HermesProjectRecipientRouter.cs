using System.IO;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Routes Supabase <c>recipient_name</c> values like <c>Hermes.Utilities</c> to a Project Manager folder.
/// Plain <c>Hermes</c> means the currently selected project (no switch).
/// </summary>
public static class HermesProjectRecipientRouter
{
    public const string HermesPrefix = "Hermes";

    /// <summary>
    /// Returns true when this recipient should be handled by Hermes.Wpf
    /// (default <c>Hermes</c> or <c>Hermes.&lt;ProjectName&gt;</c>).
    /// </summary>
    public static bool IsHermesBoundRecipient(string? recipientName, string defaultInboundName = "Hermes")
    {
        var r = (recipientName ?? string.Empty).Trim();
        if (r.Length == 0)
            return false;

        if (string.Equals(r, defaultInboundName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(r, HermesPrefix, StringComparison.OrdinalIgnoreCase))
            return true;

        return TryParseProjectSuffix(r, out _);
    }

    /// <summary>
    /// <c>Hermes.Utilities</c> → <c>Utilities</c>. Plain <c>Hermes</c> → null (keep current project).
    /// </summary>
    public static bool TryParseProjectSuffix(string? recipientName, out string projectName)
    {
        projectName = string.Empty;
        var r = (recipientName ?? string.Empty).Trim();
        if (r.Length == 0)
            return false;

        // Hermes.<name>
        if (r.StartsWith(HermesPrefix + ".", StringComparison.OrdinalIgnoreCase))
        {
            projectName = r[(HermesPrefix.Length + 1)..].Trim();
            return projectName.Length > 0;
        }

        // Hermes/<name> (Android sometimes uses slash)
        if (r.StartsWith(HermesPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            projectName = r[(HermesPrefix.Length + 1)..].Trim();
            return projectName.Length > 0;
        }

        return false;
    }

    public static HermesProject? FindInList(IEnumerable<HermesProject> projects, string projectName)
    {
        var want = (projectName ?? string.Empty).Trim();
        if (want.Length == 0)
            return null;

        return projects.FirstOrDefault(p =>
            string.Equals(p.Name, want, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Look next to known projects or under a default HermesProjects root.
    /// </summary>
    public static string? DiscoverProjectDirectory(
        IEnumerable<HermesProject> projects,
        string projectName,
        string? lastBrowsePath = null)
    {
        var want = (projectName ?? string.Empty).Trim();
        if (want.Length == 0 || want.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
        {
            var parent = Path.GetDirectoryName(p.WindowsPath);
            if (!string.IsNullOrWhiteSpace(parent))
                parents.Add(parent);
        }

        if (!string.IsNullOrWhiteSpace(lastBrowsePath))
        {
            var browseParent = Directory.Exists(lastBrowsePath)
                ? lastBrowsePath
                : Path.GetDirectoryName(lastBrowsePath);
            if (!string.IsNullOrWhiteSpace(browseParent))
                parents.Add(browseParent!);
        }

        parents.Add(@"D:\Programming\AI_Agents\HermesProjects");

        foreach (var parent in parents)
        {
            try
            {
                var candidate = Path.Combine(parent, want);
                if (Directory.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // ignore bad paths
            }
        }

        return null;
    }
}
