using System.IO;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Shared.Bridge;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.Terminals.Shared.Bridge;

namespace Hermes.TradingPlatform.Wpf.Bridge;

public sealed class TradingBridgePublisher : IDisposable
{
    private readonly TradingPlatformHost _host;
    private readonly Timer _heartbeatTimer;
    private DateTimeOffset _lastPublish = DateTimeOffset.MinValue;

    public TradingBridgePublisher(TradingPlatformHost host)
    {
        _host = host;
        _host.ReadModel.StateChanged += OnStateChanged;
        TradingBridgePaths.EnsureRoot();
        Publish();
        _heartbeatTimer = new Timer(_ => WriteHeartbeat(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPublish < TimeSpan.FromMilliseconds(400))
        {
            return;
        }

        _lastPublish = now;
        Publish();
    }

    public void Publish()
    {
        var trading = BuildSnapshot();
        TradingBridgePaths.EnsureRoot();
        var path = TradingBridgePaths.SnapshotFile;
        var existing = UnifiedSnapshotIO.Read(path);
        var unified = new UnifiedTerminalSnapshotFile
        {
            SchemaVersion = 2,
            TimestampUtc = DateTimeOffset.UtcNow,
            TradingPlatform = trading,
            SpotTerminal = existing.SpotTerminal,
            Agent = existing.Agent,
            Skills = existing.Skills,
        };
        UnifiedSnapshotIO.WriteAtomic(path, unified);
        WriteHeartbeat();
    }

    private void WriteHeartbeat()
    {
        TradingBridgePaths.EnsureRoot();
        File.WriteAllText(TradingBridgePaths.HeartbeatFile, DateTimeOffset.UtcNow.ToString("O"));
    }

    private TradingPlatformSnapshotFile BuildSnapshot()
    {
        var s = _host.StateStore.Snapshot;
        return new TradingPlatformSnapshotFile
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            TerminalRunning = true,
            MarketDataSource = _host.MarketDataSource.ToString(),
            FeedStatus = _host.FeedStatusLabel,
            Account = new AccountSnapshot
            {
                Balance = s.Account.Balance,
                Equity = s.Account.Equity,
                FreeMargin = s.Account.FreeMargin,
                UsedMargin = s.Account.UsedMargin,
                Leverage = s.Account.Leverage,
            },
            Pnl = new PnlSnapshot
            {
                Today = s.Pnl.Today,
                Week = s.Pnl.Week,
                Month = s.Pnl.Month,
                AllTime = s.Pnl.AllTime,
            },
            Risk = new RiskSnapshot
            {
                RiskLevel = s.Risk.RiskLevel.ToString(),
                DailyDrawdownPercent = s.Risk.DailyDrawdownPercent,
                ExposurePercent = s.Risk.ExposurePercent,
                SafeMode = s.Risk.SafeMode,
                EmergencyHalt = s.Risk.EmergencyHalt,
                MaxLeverage = s.Risk.MaxLeverage,
            },
            Hermes = new HermesSnapshot
            {
                State = s.Hermes.State.ToString(),
                ActiveStrategy = s.Hermes.ActiveStrategy,
                Confidence = s.Hermes.Confidence,
                CurrentReasoning = s.Hermes.CurrentReasoning,
                StrategyContext = s.Hermes.StrategyContext,
            },
            Positions = s.Positions.Select(p => new PositionSnapshot
            {
                Symbol = p.Symbol,
                Side = p.Side.ToString(),
                Size = p.Size,
                EntryPrice = p.EntryPrice,
                MarkPrice = p.MarkPrice,
                UnrealizedPnl = p.UnrealizedPnl,
            }).ToList(),
            Orders = s.Orders.Select(o => new OrderSnapshot
            {
                Id = o.Id,
                Symbol = o.Symbol,
                Type = o.Type.ToString(),
                Side = o.Side.ToString(),
                Price = o.Price,
                Quantity = o.Quantity,
                Status = o.Status.ToString(),
                ReduceOnly = o.ReduceOnly,
            }).ToList(),
            Strategies = s.Strategies.Select(st => new StrategySnapshot
            {
                Id = st.Id,
                Name = st.Name,
                Status = st.Status.ToString(),
                IsEnabled = st.IsEnabled,
            }).ToList(),
            Tickers = s.Tickers.Select(t => new MarketTickerSnapshot
            {
                Symbol = t.Symbol,
                Price = t.Price,
                ChangePercent24h = t.ChangePercent24h,
            }).ToList(),
            RecentLogs = s.Logs.Take(15).Select(l => new LogSnapshot
            {
                TimestampUtc = l.Timestamp,
                EventType = l.EventType,
                Source = l.Source,
                Message = l.Message,
            }).ToList(),
        };
    }

    public void Dispose()
    {
        _host.ReadModel.StateChanged -= OnStateChanged;
        _heartbeatTimer.Dispose();
        try
        {
            if (File.Exists(TradingBridgePaths.HeartbeatFile))
            {
                File.Delete(TradingBridgePaths.HeartbeatFile);
            }
        }
        catch
        {
            // ignore
        }
    }
}
