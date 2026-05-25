using Hermes.TradingPlatform.Core.Abstractions;

using Hermes.TradingPlatform.Core.Domain;

using Hermes.TradingPlatform.Core.Events;

using Hermes.TradingPlatform.Data;

using Hermes.TradingPlatform.Data.Persistence;

using Hermes.TradingPlatform.Data.Persistence.Sql;

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
    private RiskCircuitBreaker? _circuitBreaker;
    private readonly Dictionary<string, ITradingStrategy> _strategiesById =
        new(StringComparer.OrdinalIgnoreCase);

    public TradingPlatformHost()
    {
        EventBus = new InMemoryEventBus();
        StateStore = new TradingStateStore();
        SessionStateStore = new TradingSessionStateFileStore();

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
        ApplyAccountLeverageFromSettings(settingsDto);

        JournalStore = CreateJournalStore(settingsDto.JournalProvider);

        RiskValidator = new RiskValidator();
        Exchange = new VirtualExchangeEngine(StateStore, EventBus, RiskValidator);

        if (sessionRestored)
        {
            Exchange.RestoreOrderSequence(orderSeq);
        }

        _ = new MarketTickProjection(StateStore, EventBus);
        _ = new EventLogProjection(StateStore, EventBus);
        _ = new TradeJournalProjection(StateStore, EventBus, JournalStore);

        _persistence = new TradingStatePersistence(StateStore, EventBus, SessionStateStore, () => Exchange.NextOrderSequence);
        _sounds = new TradingSoundService(EventBus, () => PlatformSettingsStore.Load().TradingSoundsEnabled);
        _circuitBreaker = new RiskCircuitBreaker(StateStore, EventBus);

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

        StrategyParametersStore = new StrategyParametersFileStore();
        var strategies = new ITradingStrategy[]
        {
            new LiquiditySweepStrategy(),
            new MomentumStrategy(),
            new MeanReversionStrategy(),
        };

        foreach (var strategy in strategies)
        {
            _strategiesById[strategy.Id] = strategy;
        }

        ApplyPersistedStrategyParameters();

        StrategyRunner = new StrategyRunner(EventBus, StateStore, Exchange, strategies);

        HermesOrchestrator = new HermesOrchestrationService(EventBus, StateStore);
        HermesOrchestrator.SetEnabled(HermesOrchestrationEnabled);
    }

    public StrategyParametersFileStore StrategyParametersStore { get; private set; } = null!;

    public StrategyParameters GetStrategyParameters(string strategyId)
    {
        var saved = StrategyParametersStore.LoadAll();
        if (saved.TryGetValue(strategyId, out var stored))
        {
            return stored;
        }

        return _strategiesById.TryGetValue(strategyId, out var strategy)
            ? strategy.DefaultParameters
            : new StrategyParameters { StrategyId = strategyId, Quantity = 0.01m, ChangeThresholdPercent = 0.5m, CooldownSeconds = 60 };
    }

    public void UpdateStrategyParameters(StrategyParameters parameters)
    {
        if (!_strategiesById.TryGetValue(parameters.StrategyId, out var strategy))
        {
            return;
        }

        strategy.ApplyParameters(parameters);
        StrategyParametersStore.Save(parameters);

        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Strategy",
            Source = "Parameters",
            Message =
                $"Updated {parameters.StrategyId}: qty={parameters.Quantity}, "
                + $"threshold={parameters.ChangeThresholdPercent}%, cooldown={parameters.CooldownSeconds}s",
        }));
    }

    private void ApplyPersistedStrategyParameters()
    {
        var saved = StrategyParametersStore.LoadAll();
        foreach (var strategy in _strategiesById.Values)
        {
            var parameters = saved.TryGetValue(strategy.Id, out var stored)
                ? stored
                : strategy.DefaultParameters;
            strategy.ApplyParameters(parameters);
        }
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

    /// <summary>Active journal backend (JSON or SQLite). Configurable via Settings.JournalProvider.</summary>
    public IJournalStore JournalStore { get; }

    /// <summary>Backwards-compatible alias for the file path of the active journal store.</summary>
    public string JournalLocation => JournalStore.Location;

    private static IJournalStore CreateJournalStore(string provider)
    {
        return string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase)
            ? new SqliteJournalStore()
            : new TradeJournalFileWriter();
    }

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
            s.Risk.MaxRiskPerTradePercent = settings.MaxRiskPerTradePercent;
            s.Risk.MaxPositionSizeBtc = settings.MaxPositionSizeBtc;

            s.Risk.MaxLeverage = settings.MaxLeverage;

            s.Risk.MaxExposurePercent = settings.MaxExposurePercent;

            s.Risk.DefaultTakeProfitRrMultiplier = settings.DefaultTakeProfitRrMultiplier > 0
                ? settings.DefaultTakeProfitRrMultiplier
                : 2m;

            s.Risk.AutoApplyDefaultSlTp = settings.AutoApplyDefaultSlTp;

            s.Risk.SafeMode = settings.SafeMode;

            s.Risk.AutoShutdown = settings.AutoShutdown;

            s.Risk.EmergencyHalt = settings.EmergencyHalt;

        });



        RiskProfileStore.Save(StateStore.Snapshot.Risk);

        // Risk.MaxLeverage acts as a ceiling for Account.Leverage; recompute the
        // effective working leverage so UI / RiskValidator stay in sync.
        ApplyAccountLeverageFromSettings(PlatformSettingsStore.Load());
    }



    public void SetHermesOrchestrationEnabled(bool enabled)
    {
        HermesOrchestrationEnabled = enabled;
        HermesOrchestrator.SetEnabled(enabled);
        var current = PlatformSettingsStore.Load();
        PlatformSettingsStore.Save(CopySettings(current, s => s.HermesOrchestrationEnabled = enabled));

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
        PlatformSettingsStore.Save(CopySettings(current, s =>
            s.MarketDataSource = PlatformSettingsFileStore.ToStorageValue(source)));



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

            ? new BinanceFuturesMarketDataFeed(EventBus, symbols, TradingPlatformFileLogger.Instance.Info)

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



    public void ClearPlatformLogs()
    {
        StateStore.Mutate(s => s.Logs.Clear());
        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "System",
            Source = "Platform",
            Message = "Logs cleared.",
        }));
    }

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
        PlatformSettingsStore.Save(CopySettings(current, s => s.TradingSoundsEnabled = enabled));
    }

    /// <summary>Persist the journal provider choice. Takes effect on next platform start.</summary>
    public void SetJournalProvider(string provider)
    {
        var normalised = string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase) ? "Sqlite" : "Json";
        var current = PlatformSettingsStore.Load();
        PlatformSettingsStore.Save(CopySettings(current, s => s.JournalProvider = normalised));

        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "System",
            Source = "Persistence",
            Message = $"Journal provider set to {normalised} (effective on restart).",
        }));
    }

    public void SetInAppAssistantSettings(string apiKey, string model)
    {
        var current = PlatformSettingsStore.Load();
        PlatformSettingsStore.Save(CopySettings(current, s =>
        {
            s.InAppAssistantOpenRouterApiKey = apiKey?.Trim() ?? string.Empty;
            s.InAppAssistantOpenRouterModel = string.IsNullOrWhiteSpace(model) ? "openrouter/free" : model.Trim();
        }));
    }

    public void SaveAccountSettings(decimal initialBalance, decimal accountLeverage, string leverageMode)
    {
        var current = PlatformSettingsStore.Load();
        PlatformSettingsStore.Save(CopySettings(current, s =>
        {
            s.InitialAccountBalance = initialBalance;
            s.AccountLeverage = accountLeverage;
            s.LeverageMode = string.IsNullOrWhiteSpace(leverageMode) ? "Fixed" : leverageMode.Trim();
        }));
        ApplyAccountLeverageFromSettings(PlatformSettingsStore.Load());
    }

    public void ResetPaperAccount()
    {
        var settings = PlatformSettingsStore.Load();
        var leverage = ResolveEffectiveLeverage(settings);
        var clean = InitialTradingSeed.CreateClean(settings.InitialAccountBalance, leverage);

        StateStore.Initialize(clean);
        Exchange.RestoreOrderSequence(1000);
        JournalStore.Clear();
        SessionStateStore.Save(StateStore.Snapshot, Exchange.NextOrderSequence);

        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "System",
            Source = "Platform",
            Message = $"Paper account reset: balance={settings.InitialAccountBalance:N2}, leverage={leverage:F1}x",
        }));
    }

    private void ApplyAccountLeverageFromSettings(PlatformSettingsDto settings)
    {
        var leverage = ResolveEffectiveLeverage(settings);
        StateStore.Mutate(s => s.Account.Leverage = leverage);
    }

    private decimal ResolveEffectiveLeverage(PlatformSettingsDto settings)
    {
        var maxAllowed = StateStore.Snapshot.Risk.MaxLeverage;
        if (maxAllowed <= 0)
        {
            maxAllowed = 1m;
        }

        if (string.Equals(settings.LeverageMode, "Maximum", StringComparison.OrdinalIgnoreCase))
        {
            return maxAllowed;
        }

        var fixedLeverage = settings.AccountLeverage > 0 ? settings.AccountLeverage : 3m;
        // Hard-cap at Risk.MaxLeverage so Account.Leverage never exceeds the risk ceiling.
        return Math.Min(fixedLeverage, maxAllowed);
    }

    private static PlatformSettingsDto CopySettings(
        PlatformSettingsDto current,
        Action<PlatformSettingsDto>? mutate)
    {
        var copy = new PlatformSettingsDto
        {
            MarketDataSource = current.MarketDataSource,
            HermesOrchestrationEnabled = current.HermesOrchestrationEnabled,
            TradingSoundsEnabled = current.TradingSoundsEnabled,
            InAppAssistantOpenRouterApiKey = current.InAppAssistantOpenRouterApiKey,
            InAppAssistantOpenRouterModel = string.IsNullOrWhiteSpace(current.InAppAssistantOpenRouterModel)
                ? "openrouter/free"
                : current.InAppAssistantOpenRouterModel,
            InitialAccountBalance = current.InitialAccountBalance,
            AccountLeverage = current.AccountLeverage,
            LeverageMode = string.IsNullOrWhiteSpace(current.LeverageMode) ? "Fixed" : current.LeverageMode,
            JournalProvider = string.IsNullOrWhiteSpace(current.JournalProvider) ? "Json" : current.JournalProvider,
        };
        mutate?.Invoke(copy);
        return copy;
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


