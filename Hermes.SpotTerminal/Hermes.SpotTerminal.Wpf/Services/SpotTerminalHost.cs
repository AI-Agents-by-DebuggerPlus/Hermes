using Hermes.SpotTerminal.Agent;
using Hermes.SpotTerminal.Agent.Skills;
using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Core.Events;
using Hermes.SpotTerminal.Data;
using Hermes.SpotTerminal.Data.Persistence;
using Hermes.SpotTerminal.Data.Projections;
using Hermes.SpotTerminal.Data.Seed;
using Hermes.SpotTerminal.Exchange.Binance;
using Hermes.SpotTerminal.Exchange.Virtual;
using Hermes.SpotTerminal.Shared.Settings;

namespace Hermes.SpotTerminal.Wpf.Services;

public sealed class SpotTerminalHost : IDisposable
{
    private IMarketDataFeed? _feed;
    private ISpotExecutionGateway? _gateway;
    private BinanceSpotExecutionGateway? _binanceGateway;
    private readonly Timer? _persistTimer;

    public SpotTerminalHost()
    {
        EventBus = new InMemoryEventBus();
        StateStore = new SpotStateStore();
        SessionStore = new SpotSessionStateFileStore();
        SettingsStore = new SpotPlatformSettingsFileStore();
        AgentEvents = new AgentEventsJsonlStore();
        Skills = new SkillJsonRepository();
        Journal = new LearningJournalJsonlStore();

        if (!SessionStore.TryLoad(out var loaded))
        {
            loaded = InitialSpotSeed.Create();
        }

        StateStore.Initialize(loaded);

        var settings = SettingsStore.Load();
        ExecutionMode = SpotPlatformSettingsFileStore.ParseMode(settings.ExecutionMode);

        _ = new MarketTickProjection(StateStore, EventBus);
        _ = new EventLogProjection(StateStore, EventBus);
        _ = new AgentEventProjection(StateStore, EventBus, AgentEvents);

        AgentMonitor = new AgentMonitoringService(EventBus, StateStore);
        SkillLifecycle = new SkillLifecycleService(Skills, StateStore, EventBus, AgentMonitor);

        SyncSkillsFromRepo();
        WireExecution(settings);
        ReadModel = new SpotReadModel(StateStore);

        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "System",
            Source = "System",
            Message = $"Spot Terminal started · mode={ExecutionMode}",
        }));

        _persistTimer = new Timer(_ => SessionStore.Save(StateStore.Snapshot), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
    }

    public IEventBus EventBus { get; }
    public ISpotStateStore StateStore { get; }
    public SpotSessionStateFileStore SessionStore { get; }
    public SpotPlatformSettingsFileStore SettingsStore { get; }
    public IAgentEventStore AgentEvents { get; }
    public ISkillRepository Skills { get; }
    public ILearningJournalStore Journal { get; }
    public IAgentMonitoringService AgentMonitor { get; }
    public SkillLifecycleService SkillLifecycle { get; }
    public SpotReadModel ReadModel { get; }
    public ExecutionMode ExecutionMode { get; private set; }
    public ISpotExecutionGateway Gateway => _gateway ?? throw new InvalidOperationException("Gateway not initialized");

    public event EventHandler? FeedStatusChanged;

    public string FeedStatusLabel => StateStore.Snapshot.FeedStatus;

    public void Start()
    {
        var settings = SettingsStore.Load();
        var symbols = settings.WatchSymbols?.ToList() ?? ["BTCUSDT", "ETHUSDT", "SOLUSDT"];
        _ = _feed?.StartAsync(symbols);
    }

    public void SetExecutionMode(ExecutionMode mode)
    {
        ExecutionMode = mode;
        var settings = SettingsStore.Load();
        settings.ExecutionMode = mode.ToString();
        SettingsStore.Save(settings);
        WireExecution(settings);
        StateStore.Mutate(s => s.Mode = mode);
    }

    public void UpdateBinanceCredentials(string apiKey, string apiSecret)
    {
        var settings = SettingsStore.Load();
        settings.BinanceApiKey = apiKey ?? "";
        settings.BinanceApiSecret = apiSecret ?? "";
        SettingsStore.Save(settings);

        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Settings",
            Source = "System",
            Message = "Binance API keys updated (stored to platform-settings.json)",
        }));

        if (ExecutionMode == ExecutionMode.SpotDemo)
        {
            WireExecution(settings);
            _ = _binanceGateway?.GetBalancesAsync();
        }
    }

    private void WireExecution(SpotPlatformSettingsDto settings)
    {
        _feed?.Dispose();
        _binanceGateway?.Dispose();

        if (ExecutionMode == ExecutionMode.SpotDemo)
        {
            _binanceGateway = new BinanceSpotExecutionGateway(settings.BinanceApiKey, settings.BinanceApiSecret, EventBus, StateStore);
            _gateway = _binanceGateway;
            _feed = new BinanceSpotMarketDataFeed(settings.BinanceApiKey, settings.BinanceApiSecret, EventBus, StateStore);
            _ = _binanceGateway.GetBalancesAsync();
        }
        else
        {
            _gateway = new VirtualSpotExchange(StateStore, EventBus);
            _feed = new VirtualSpotMarketDataFeed(EventBus, StateStore);
        }
    }

    private void SyncSkillsFromRepo()
    {
        var fromRepo = Skills.LoadAll();
        if (fromRepo.Count == 0)
        {
            foreach (var s in StateStore.Snapshot.Skills)
            {
                Skills.Save(s);
            }
        }
        else
        {
            StateStore.Mutate(s =>
            {
                s.Skills.Clear();
                s.Skills.AddRange(fromRepo);
            });
        }
    }

    public void Dispose()
    {
        _persistTimer?.Dispose();
        _feed?.Dispose();
        _binanceGateway?.Dispose();
        SessionStore.Save(StateStore.Snapshot);
    }
}
