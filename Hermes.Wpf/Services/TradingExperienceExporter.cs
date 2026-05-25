using System.Globalization;
using System.IO;
using System.Text;
using Hermes.TradingPlatform.Shared.Bridge;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class TradingExperienceExporter
{
    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;
    private readonly Func<ExternalBrainService?> _brain;
    private TradingPlatformSnapshotFile? _previousSnapshot;
    private decimal _peakEquity;

    public TradingExperienceExporter(
        LogService log,
        Func<HermesSettings> settings,
        Func<ExternalBrainService?> brain)
    {
        _log = log;
        _settings = settings;
        _brain = brain;
    }

    public void AttachToBridge(TradingPlatformBridgeService bridge)
    {
        bridge.SnapshotUpdated += OnSnapshotUpdated;
    }

    private void OnSnapshotUpdated(TradingPlatformSnapshotFile snap)
    {
        if (!_settings().TradingExperienceExportEnabled)
        {
            _previousSnapshot = snap;
            return;
        }

        var brain = _brain();
        var vault = brain?.ResolveEffectiveMemoryPath();
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
        {
            _previousSnapshot = snap;
            return;
        }

        try
        {
            DetectAndExport(snap, vault);
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[trading-experience] export failed: {ex.Message}");
        }

        _previousSnapshot = snap;
    }

    private void DetectAndExport(TradingPlatformSnapshotFile snap, string vault)
    {
        if (snap.Risk.EmergencyHalt && (_previousSnapshot is null || !_previousSnapshot.Risk.EmergencyHalt))
        {
            WriteEpisode(vault, TradeEventKind.EmergencyStop, snap, "Emergency halt activated.");
        }

        if (snap.Account.Equity > _peakEquity)
        {
            _peakEquity = snap.Account.Equity;
        }

        var ddThreshold = (decimal)_settings().TradingExperienceDrawdownThreshold;
        if (_peakEquity > 0 && snap.Account.Equity < _peakEquity * (1m - ddThreshold))
        {
            WriteEpisode(
                vault,
                TradeEventKind.DrawdownThreshold,
                snap,
                $"Equity {snap.Account.Equity:N2} below peak {_peakEquity:N2} (threshold {ddThreshold:P0}).");
        }

        var pnlThreshold = (decimal)_settings().TradingExperiencePnlThreshold;
        if (_previousSnapshot is not null
            && Math.Abs(snap.Pnl.Today - _previousSnapshot.Pnl.Today) >= pnlThreshold)
        {
            var delta = snap.Pnl.Today - _previousSnapshot.Pnl.Today;
            WriteEpisode(
                vault,
                TradeEventKind.LargeRealizedPnl,
                snap,
                $"Daily PnL change {delta:N2} (threshold {pnlThreshold:N2}).");
        }

        if (_previousSnapshot is not null)
        {
            foreach (var strategy in snap.Strategies)
            {
                var prev = _previousSnapshot.Strategies.FirstOrDefault(s =>
                    string.Equals(s.Id, strategy.Id, StringComparison.OrdinalIgnoreCase));
                if (prev is not null && prev.IsEnabled != strategy.IsEnabled)
                {
                    WriteEpisode(
                        vault,
                        TradeEventKind.StrategyEnabled,
                        snap,
                        $"Strategy {strategy.Id} → {(strategy.IsEnabled ? "enabled" : "disabled")}.");
                }
            }
        }
    }

    public void RecordRiskRejection(string reason, TradingPlatformSnapshotFile? snap)
    {
        if (!_settings().TradingExperienceExportEnabled)
        {
            return;
        }

        var brain = _brain();
        var vault = brain?.ResolveEffectiveMemoryPath();
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault) || snap is null)
        {
            return;
        }

        WriteEpisode(vault, TradeEventKind.RiskRejection, snap, reason);
    }

    private void WriteEpisode(
        string vaultRoot,
        TradeEventKind kind,
        TradingPlatformSnapshotFile snap,
        string description)
    {
        var dir = Path.Combine(vaultRoot, "Knowledge", "Trading", "Episodes");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow;
        var fileName = $"{stamp:yyyy-MM-dd_HH-mm-ss}_{kind}.md";
        var path = Path.Combine(dir, fileName);
        var importance = kind is TradeEventKind.EmergencyStop or TradeEventKind.LargeRealizedPnl ? 4 : 3;
        var body = BuildEpisodeMarkdown(kind, snap, description, importance, stamp);
        File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _log.LogInfo($"[trading-experience] Captured {kind} episode: {fileName}");
        _brain()?.RestartWatcherAndReload("trading-experience");
    }

    private static string BuildEpisodeMarkdown(
        TradeEventKind kind,
        TradingPlatformSnapshotFile snap,
        string description,
        int importance,
        DateTime stampUtc)
    {
        var iso = stampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var strategies = string.Join(", ", snap.Strategies.Select(s => $"{s.Id}({(s.IsEnabled ? "on" : "off")})"));
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: episodic");
        sb.AppendLine("role: Trader");
        sb.AppendLine($"tags: [trading, episode, {kind.ToString().ToLowerInvariant()}]");
        sb.AppendLine($"importance: {importance}");
        sb.AppendLine("captured: auto");
        sb.AppendLine($"date: {iso}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {kind}");
        sb.AppendLine();
        sb.AppendLine($"**Event:** {description}");
        sb.AppendLine(
            $"**Context:** Balance={snap.Account.Balance:N2}, Equity={snap.Account.Equity:N2}, Open positions={snap.Positions.Count}");
        sb.AppendLine($"**Outcome:** {snap.Hermes.CurrentReasoning}");
        sb.AppendLine($"**Active strategies:** {strategies}");
        sb.AppendLine();
        sb.AppendLine("## Lesson prompt");
        sb.AppendLine("<!-- Hermes заполнит при следующем обращении в режиме Trader -->");
        return sb.ToString();
    }
}

public enum TradeEventKind
{
    LargeRealizedPnl,
    RiskRejection,
    EmergencyStop,
    StrategyEnabled,
    DrawdownThreshold,
}
