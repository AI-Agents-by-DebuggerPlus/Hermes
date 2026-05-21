using Hermes.TradingPlatform.Core.Abstractions;

using Hermes.TradingPlatform.Core.Domain;

using Hermes.TradingPlatform.Core.Events;

using Hermes.TradingPlatform.Data;

using Hermes.TradingPlatform.Data.Persistence;

using Hermes.TradingPlatform.Data.Projections;

using Hermes.TradingPlatform.Data.Seed;

using Hermes.TradingPlatform.Exchange;

using Hermes.TradingPlatform.Exchange.MarketData;

using Hermes.TradingPlatform.Risk;

using Hermes.TradingPlatform.Shared.Risk;

using Hermes.TradingPlatform.Shared.Settings;
using Hermes.TradingPlatform.Orchestration;
using Hermes.TradingPlatform.Strategies;
using Hermes.TradingPlatform.Strategies.BuiltIn;

namespace Hermes.TradingPlatform.Wpf.Services;



public sealed class TradingPlatformHost : IDisposable

{

    private IMarketDataFeed? _activeFeed;
    private TradingStatePersistence? _persistence;
    private TradingSoundService? _sounds;

    public TradingPlatformHost()
    {
        EventBus = new InMemoryEventBus();
        StateStore = new TradingStateStore();
        SessionStateStore = new TradingSessionStateFileStore();
        JournalFileWriter = new TradeJournalFileWriter();

        TradingPlatformState loadedState;
        var sessionRestored = SessionStateStore.TryLoad(out loadedState, out var orderSeq);
        if (!sessionRestored)
        {
            loadedState = InitialTradingSeed.Create();
        }

        StateStore.Initialize(loadedState);

        RiskProfileStore = new RiskProfileFileStore();
        LoadPersistedRiskProfile();

        PlatformSettingsStore = new PlatformSettingsFileStore();
        var settingsDto = PlatformSettingsStore.Load();
        MarketDataSource = PlatformSettingsFileStore.ParseSource(settingsDto.MarketDataSource);
        HermesOrchestrationEnabled = settingsDto.HermesOrchestrationEnabled;

        RiskValidator = new RiskValidator();
        Exchange = new VirtualExchangeEngine(StateStore, EventBus, RiskValidator);

        if (sessionRestored)
        {
            Exchange.RestoreOrderSequence(orderSeq);
        }

        _ = new MarketTickProjection(StateStore, EventBus);
        _ = new EventLogProjection(StateStore, EventBus);
        _ = new TradeJournalProjection(StateStore, EventBus, JournalFileWriter);

        _persistence = new TradingStatePersistence(StateStore, EventBus, SessionStateStore, () => Exchange.NextOrderSequence);
        _sounds = new TradingSoundService(EventBus, () => PlatformSettingsStore.Load().TradingSoundsEnabled);

        if (sessionRestored)
        {
            var snap = StateStore.Snapshot;
            EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                EventType = "System",
                Source = "Persistence",
                Message =
                    $"Session restored: balance={snap.Account.Balance:N2} positions={snap.Positions.Count} "
                    + $"journal={snap.Journal.Count} orders={snap.Orders.Count}",
            }));
        }

        ReadModel = new TradingReadModel(StateStore);

        StrategyRunner = new StrategyRunner(
            EventBus,
            StateStore,
            Exchange,
            [
                new LiquiditySweepStrategy(),
                new MomentumStrategy(),
                new MeanReversionStrategy(),
            ]);

        HermesOrchestrator = new HermesOrchestrationService(EventBus, StateStore);
        HermesOrchestrator.SetEnabled(HermesOrchestrationEnabled);
    }

    public IEventBus EventBus { get; }

    public ITradingStateStore StateStore { get; }

    public IRiskValidator RiskValidator { get; }

    public IVirtualExchange Exchange { get; }

    public IMarketDataFeed? ActiveFeed => _activeFeed;

    public TradingReadModel ReadModel { get; }
    public StrategyRunner StrategyRunner { get; }
    public IHermesOrchestrator HermesOrchestrator { get; }
    public bool HermesOrchestrationEnabled { get; private set; }

    public RiskProfileFileStore RiskProfileStore { get; }

    public PlatformSettingsFileStore PlatformSettingsStore { get; }

    public TradingSessionStateFileStore SessionStateStore { get; }

    public TradeJournalFileWriter JournalFileWriter { get; }

    public MarketDataSource MarketDataSource { get; private set; }



    public event EventHandler? FeedStatusChanged;



    public string FeedStatusLabel => _activeFeed?.Status switch

    {

        MarketFeedStatus.Connected => MarketDataSource == MarketDataSource.BinanceFutures ? "BINANCE LIVE" : "SIMULATION",

        MarketFeedStatus.Connecting => "CONNECTING…",

        MarketFeedStatus.Reconnecting => "RECONNECTING…",

        MarketFeedStatus.Error => "FEED ERROR",

        _ => "STOPPED",

    };



    public string MarketDataEndpoint => MarketDataSource == MarketDataSource.BinanceFutures

        ? "wss://fstream.binance.com (USDT-M Futures)"

        : "internal://mock-random-walk";



    public void SetStrategyEnabled(string strategyId, bool enabled)
    {
        StateStore.Mutate(s =>
        {
            var strategy = s.Strategies.FirstOrDefault(x => x.Id == strategyId);
            if (strategy is null)
            {
                return;
            }

            strategy.IsEnabled = enabled;
            strategy.Status = enabled && !s.Risk.EmergencyHalt
                ? StrategyRunStatus.Running
                : StrategyRunStatus.Idle;
        });

        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Strategy",
            Source = "StrategyRunner",
            Message = $"Strategy {strategyId} {(enabled ? "enabled" : "disabled")}",
        }));
    }

    public void PersistRiskSettings(RiskProfileSettingsDto settings)

    {

        StateStore.Mutate(s =>

        {

            s.Risk.MaxDailyLossPercent = settings.MaxDailyLossPercent;

            s.Risk.MaxPositionSizeBtc = settings.MaxPositionSizeBtc;

            s.Risk.MaxLeverage = settings.MaxLeverage;

            s.Risk.MaxExposurePercent = settings.MaxExposurePercent;

            s.Risk.SafeMode = settings.SafeMode;

            s.Risk.AutoShutdown = settings.AutoShutdown;

            s.Risk.EmergencyHalt = settings.EmergencyHalt;

        });



        RiskProfileStore.Save(StateStore.Snapshot.Risk);

    }



    public void SetHermesOrchestrationEnabled(bool enabled)
    {
        HermesOrchestrationEnabled = enabled;
        HermesOrchestrator.SetEnabled(enabled);
        var current = PlatformSettingsStore.Load();
        PlatformSettingsStore.Save(new PlatformSettingsDto
        {
            MarketDataSource = current.MarketDataSource,
            HermesOrchestrationEnabled = enabled,
            TradingSoundsEnabled = current.TradingSoundsEnabled,
        });

        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Hermes",
            Source = "Orchestrator",
            Message = enabled ? "Hermes orchestration enabled." : "Hermes orchestration disabled.",
        }));
    }

    public void SetMarketDataSource(MarketDataSource source)
    {
        MarketDataSource = source;
        var current = PlatformSettingsStore.Load();
        PlatformSettingsStore.Save(new PlatformSettingsDto
        {
            MarketDataSource = PlatformSettingsFileStore.ToStorageValue(source),
            HermesOrchestrationEnabled = current.HermesOrchestrationEnabled,
            TradingSoundsEnabled = current.TradingSoundsEnabled,
        });



        RestartMarketFeed();

    }



    private void LoadPersistedRiskProfile() =>

        StateStore.Mutate(s => RiskProfileStore.TryApplyTo(s.Risk));



    public void Start() => RestartMarketFeed();



    private void RestartMarketFeed()

    {

        if (_activeFeed is not null)

        {

            _activeFeed.StatusChanged -= OnFeedStatusChanged;

            _activeFeed.Dispose();

            _activeFeed = null;

        }



        var symbols = StateStore.Snapshot.Tickers.Select(t => t.Symbol).ToList();

        var seed = StateStore.Snapshot.Tickers.Select(t => (t.Symbol, t.Price));



        _activeFeed = MarketDataSource == MarketDataSource.BinanceFutures

            ? new BinanceFuturesMarketDataFeed(EventBus, symbols)

            : new MockMarketDataFeed(EventBus, seed);



        _activeFeed.StatusChanged += OnFeedStatusChanged;

        _activeFeed.Start();

        OnFeedStatusChanged(_activeFeed, EventArgs.Empty);



        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry

        {

            Timestamp = DateTimeOffset.UtcNow,

            EventType = "System",

            Source = "Platform",

            Message = $"Market data: {_activeFeed.Name} ({PlatformSettingsFileStore.ToDisplayName(MarketDataSource)})",

        }));

    }



    private void OnFeedStatusChanged(object? sender, EventArgs e) => FeedStatusChanged?.Invoke(this, EventArgs.Empty);



    public void EmergencyStop(string reason)

    {

        StateStore.Mutate(s =>

        {

            s.Risk.EmergencyHalt = true;

            s.Risk.RiskLevel = RiskLevel.Critical;

            foreach (var strategy in s.Strategies)

            {

                strategy.Status = StrategyRunStatus.Halted;

            }

        });



        EventBus.Publish(new RiskTriggeredEvent(reason, EmergencyHalt: true));

    }



    public void SetTradingSoundsEnabled(bool enabled)
    {
        var current = PlatformSettingsStore.Load();
        PlatformSettingsStore.Save(new PlatformSettingsDto
        {
            MarketDataSource = current.MarketDataSource,
            HermesOrchestrationEnabled = current.HermesOrchestrationEnabled,
            TradingSoundsEnabled = enabled,
        });
    }

    public void Dispose()
    {
        _persistence?.Dispose();
        _sounds?.Dispose();

        if (_activeFeed is not null)
        {
            _activeFeed.StatusChanged -= OnFeedStatusChanged;
            _activeFeed.Dispose();
        }

        if (Exchange is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

}


