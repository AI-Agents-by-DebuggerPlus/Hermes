using System.IO;
using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Shared.Bridge;
using Hermes.SpotTerminal.Wpf.Services;
using Hermes.Terminals.Shared.Bridge;

namespace Hermes.SpotTerminal.Wpf.Bridge;

public sealed class SpotBridgePublisher : IDisposable
{
    private readonly SpotTerminalHost _host;
    private readonly Timer _heartbeatTimer;
    private DateTimeOffset _lastPublish = DateTimeOffset.MinValue;

    public SpotBridgePublisher(SpotTerminalHost host)
    {
        _host = host;
        _host.ReadModel.StateChanged += (_, _) => OnStateChanged();
        UnifiedBridgePaths.EnsureTradingBridgeRoot();
        Publish();
        _heartbeatTimer = new Timer(_ => WriteHeartbeat(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    private void OnStateChanged()
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
        var path = UnifiedBridgePaths.UnifiedSnapshotFile;
        var existing = UnifiedSnapshotIO.Read(path);
        var snap = _host.StateStore.Snapshot;

        var unified = new UnifiedTerminalSnapshotFile
        {
            SchemaVersion = 2,
            TimestampUtc = DateTimeOffset.UtcNow,
            TradingPlatform = existing.TradingPlatform,
            SpotTerminal = new SpotTerminalSnapshotSection
            {
                TerminalRunning = true,
                ExecutionMode = snap.Mode.ToString(),
                FeedStatus = snap.FeedStatus,
                Balances = snap.Balances.Select(b => new SpotBalanceSnapshot
                {
                    Asset = b.Asset, Free = b.Free, Locked = b.Locked,
                }).ToList(),
                OpenOrders = snap.Orders
                    .Where(o => o.Status == SpotOrderStatus.Open)
                    .Select(o => new SpotOrderSnapshot
                    {
                        Id = o.Id, Symbol = o.Symbol, Side = o.Side.ToString(),
                        Type = o.Type.ToString(), Price = o.Price, Quantity = o.Quantity,
                        Status = o.Status.ToString(),
                    }).ToList(),
                Tickers = snap.Tickers.Select(t => new SpotTickerSnapshot
                {
                    Symbol = t.Symbol, Price = t.Price, ChangePercent24h = t.ChangePercent24h,
                }).ToList(),
            },
            Agent = new AgentSnapshotSection
            {
                SessionId = snap.Agent.Id,
                SessionState = snap.Agent.State,
                ActiveSkillId = snap.Agent.ActiveSkillId,
                CurrentThought = snap.Agent.CurrentThought,
                RecentEvents = snap.AgentEvents.Take(20).Select(e => new AgentEventSnapshot
                {
                    TimestampUtc = e.TimestampUtc,
                    Kind = e.Kind.ToString(),
                    Summary = e.Summary,
                    Symbol = e.Symbol,
                }).ToList(),
            },
            Skills = new SkillsSnapshotSection
            {
                DraftCount = snap.Skills.Count(s => s.Status == SkillStatus.Draft),
                ApprovedCount = snap.Skills.Count(s => s.Status == SkillStatus.Approved),
                Skills = snap.Skills.Select(s => new SkillSnapshot
                {
                    Id = s.Id, Name = s.Name, Status = s.Status.ToString(), IsInitial = s.IsInitial,
                }).ToList(),
            },
        };

        UnifiedSnapshotIO.WriteAtomic(path, unified);
        WriteHeartbeat();
    }

    private static void WriteHeartbeat()
    {
        UnifiedBridgePaths.EnsureTradingBridgeRoot();
        File.WriteAllText(UnifiedBridgePaths.UnifiedHeartbeatFile, DateTimeOffset.UtcNow.ToString("O"));
    }

    public void Dispose()
    {
        _heartbeatTimer.Dispose();
        try
        {
            if (File.Exists(UnifiedBridgePaths.UnifiedHeartbeatFile))
            {
                File.Delete(UnifiedBridgePaths.UnifiedHeartbeatFile);
            }
        }
        catch { /* ignore */ }
    }
}
