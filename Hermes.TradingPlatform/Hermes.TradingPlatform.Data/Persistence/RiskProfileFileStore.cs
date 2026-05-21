using System.Text.Json;
using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Data.Persistence;

public sealed class RiskProfileFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public RiskProfileFileStore(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesTrading");
        Directory.CreateDirectory(dir);
        FilePath = filePath ?? Path.Combine(dir, "risk-profile.json");
    }

    public string FilePath { get; }

    public void Save(RiskProfile risk)
    {
        var model = new RiskProfileFileModel
        {
            MaxDailyLossPercent = risk.MaxDailyLossPercent,
            MaxPositionSizeBtc = risk.MaxPositionSizeBtc,
            MaxLeverage = risk.MaxLeverage,
            MaxExposurePercent = risk.MaxExposurePercent,
            SafeMode = risk.SafeMode,
            AutoShutdown = risk.AutoShutdown,
            EmergencyHalt = risk.EmergencyHalt,
        };

        File.WriteAllText(FilePath, JsonSerializer.Serialize(model, JsonOptions));
    }

    public bool TryApplyTo(RiskProfile risk)
    {
        if (!File.Exists(FilePath))
        {
            return false;
        }

        try
        {
            var model = JsonSerializer.Deserialize<RiskProfileFileModel>(File.ReadAllText(FilePath), JsonOptions);
            if (model is null)
            {
                return false;
            }

            ApplyTo(model, risk);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyTo(RiskProfileFileModel file, RiskProfile risk)
    {
        risk.MaxDailyLossPercent = file.MaxDailyLossPercent;
        risk.MaxPositionSizeBtc = file.MaxPositionSizeBtc;
        risk.MaxLeverage = file.MaxLeverage;
        risk.MaxExposurePercent = file.MaxExposurePercent;
        risk.SafeMode = file.SafeMode;
        risk.AutoShutdown = file.AutoShutdown;
        risk.EmergencyHalt = file.EmergencyHalt;
    }

    private sealed class RiskProfileFileModel
    {
        public decimal MaxDailyLossPercent { get; set; } = 5m;
        public decimal MaxPositionSizeBtc { get; set; } = 0.5m;
        public decimal MaxLeverage { get; set; } = 5m;
        public decimal MaxExposurePercent { get; set; } = 50m;
        public bool SafeMode { get; set; } = true;
        public bool AutoShutdown { get; set; } = true;
        public bool EmergencyHalt { get; set; }
    }
}
