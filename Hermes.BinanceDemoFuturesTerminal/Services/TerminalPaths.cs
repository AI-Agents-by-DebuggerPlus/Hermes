using System.IO;



namespace Hermes.BinanceDemoFuturesTerminal.Services;



/// <summary>Paths for Hermes.BinanceDemoFuturesTerminal (logs + settings file).</summary>

public static class TerminalPaths

{

    private const string DefaultDevRoot = @"D:\Programming\AI_Agents\Hermes\Hermes.BinanceDemoFuturesTerminal";

    private const string DefaultLogsRoot = @"D:\Programming\AI_Agents\Hermes\Logs";

    private const string LogsSubfolder = "Hermes.BinanceDemoFuturesTerminal";



    public static string ProjectRoot

    {

        get

        {

            var env = Environment.GetEnvironmentVariable("HERMES_FUTURES_TERMINAL_ROOT")?.Trim();

            if (!string.IsNullOrEmpty(env))

            {

                return env;

            }



            if (Directory.Exists(DefaultDevRoot))

            {

                return DefaultDevRoot;

            }



            return AppDomain.CurrentDomain.BaseDirectory;

        }

    }



    public static string LogsDirectory

    {

        get

        {

            var root = Environment.GetEnvironmentVariable("HERMES_LOGS_ROOT")?.Trim();

            if (string.IsNullOrEmpty(root))

            {

                root = DefaultLogsRoot;

            }



            var dir = Path.Combine(root, LogsSubfolder);

            Directory.CreateDirectory(dir);

            return dir;

        }

    }



    public static string SettingsFile => Path.Combine(ProjectRoot, "platform-settings.json");

}


