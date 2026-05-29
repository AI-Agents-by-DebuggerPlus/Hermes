using Hermes.SpotTerminal.Shared.Bridge;
using Hermes.SpotTerminal.Shared.Settings;

namespace Hermes.SpotTerminal.Data.Persistence;

public sealed class SpotPlatformSettingsFileStore
{
    public string FilePath => Path.Combine(SpotBridgePaths.DataRoot, "platform-settings.json");

    public SpotPlatformSettingsDto Load()
    {
        SpotBridgePaths.EnsureRoot();
        return AtomicJsonFileStore.TryLoad(FilePath, out SpotPlatformSettingsDto? dto) && dto is not null
            ? dto
            : new SpotPlatformSettingsDto();
    }

    public void Save(SpotPlatformSettingsDto dto)
    {
        SpotBridgePaths.EnsureRoot();
        AtomicJsonFileStore.Save(FilePath, dto);
    }

    public static Core.Enums.ExecutionMode ParseMode(string? s) =>
        string.Equals(s, "SpotDemo", StringComparison.OrdinalIgnoreCase)
            ? Core.Enums.ExecutionMode.SpotDemo
            : Core.Enums.ExecutionMode.Virtual;
}
