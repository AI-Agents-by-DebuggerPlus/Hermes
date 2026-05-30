using Hermes.BinanceDemoFuturesTerminal.Helpers;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public static class AppServices
{
    public static TerminalLogService Log { get; } = new();

    public static PlatformSettings Settings { get; private set; } = ConfigManager.LoadSettings();

    public static void ReloadSettings() => Settings = ConfigManager.LoadSettings();

    public static void SaveSettings(PlatformSettings settings)
    {
        Settings = settings;
        ConfigManager.SaveSettings(settings);
    }
}
