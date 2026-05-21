using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Mapping;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Shared.Risk;

namespace Hermes.TradingPlatform.Wpf.Services;

/// <summary>UI-facing read model over <see cref="ITradingStateStore"/> (Phase 2 bridge).</summary>
public sealed class TradingReadModel
{
    private readonly ITradingStateStore _store;

    public TradingReadModel(ITradingStateStore store)
    {
        _store = store;
        _store.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? StateChanged;

    public AccountSummaryDto GetAccountSummary() => TradingUiMapper.ToDto(_store.Snapshot.Account);

    public PnlSummaryDto GetPnlSummary() => TradingUiMapper.ToDto(_store.Snapshot.Pnl);

    public IReadOnlyList<PositionDto> GetOpenPositions() =>
        _store.Snapshot.Positions.Select(TradingUiMapper.ToDto).ToList();

    public IReadOnlyList<OrderDto> GetActiveOrders() =>
        _store.Snapshot.Orders
            .Where(o => o.Status == Core.Domain.OrderStatus.Open)
            .Select(TradingUiMapper.ToDto)
            .ToList();

    public IReadOnlyList<OrderDto> GetAllOrders() =>
        _store.Snapshot.Orders.Select(TradingUiMapper.ToDto).ToList();

    public RiskStatusDto GetRiskStatus()
    {
        var snapshot = _store.Snapshot;
        return TradingUiMapper.ToDto(snapshot.Risk, snapshot.Account.Leverage);
    }

    public RiskProfileSettingsDto GetRiskSettings() =>
        TradingUiMapper.ToSettingsDto(_store.Snapshot.Risk);

    public HermesStatusDto GetHermesStatus() => TradingUiMapper.ToDto(_store.Snapshot.Hermes);

    public IReadOnlyList<StrategyCardDto> GetStrategies() =>
        _store.Snapshot.Strategies.Select(TradingUiMapper.ToDto).ToList();

    public IReadOnlyList<MarketTickerDto> GetMarketWatch() =>
        _store.Snapshot.Tickers.Select(TradingUiMapper.ToDto).ToList();

    public IReadOnlyList<LogEntryDto> GetLogs() =>
        _store.Snapshot.Logs.Select(TradingUiMapper.ToDto).ToList();

    public IReadOnlyList<TradeJournalEntryDto> GetJournal() =>
        _store.Snapshot.Journal.Select(TradingUiMapper.ToDto).ToList();

    public IReadOnlyList<HermesTaskDto> GetHermesTasks() =>
        _store.Snapshot.Hermes.Tasks.Select(TradingUiMapper.ToDto).ToList();

    public IReadOnlyList<HermesDecisionDto> GetHermesDecisions() =>
        _store.Snapshot.Hermes.Decisions.Select(TradingUiMapper.ToDto).ToList();

    public string GetHermesReasoning() => _store.Snapshot.Hermes.CurrentReasoning;

    public string GetHermesStrategyContext() => _store.Snapshot.Hermes.StrategyContext;
}
