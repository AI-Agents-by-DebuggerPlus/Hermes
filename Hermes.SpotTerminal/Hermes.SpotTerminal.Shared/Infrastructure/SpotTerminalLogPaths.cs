namespace Hermes.SpotTerminal.Shared.Infrastructure;

/// <summary>File log root for Hermes.SpotTerminal (override via HERMES_SPOT_LOGS_ROOT).</summary>
public static class SpotTerminalLogPaths
{
    private const string DefaultRoot = @"D:\Programming\AI_Agents\Hermes\Docs\Logs\SpotTerminal";

    public static string Root
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("HERMES_SPOT_LOGS_ROOT")?.Trim();
            var root = string.IsNullOrEmpty(env) ? DefaultRoot : env;
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
