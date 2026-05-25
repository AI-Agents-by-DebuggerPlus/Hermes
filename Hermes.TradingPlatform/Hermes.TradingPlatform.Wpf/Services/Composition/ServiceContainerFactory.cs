using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Events;
using Hermes.TradingPlatform.Wpf.Bridge;
using Hermes.TradingPlatform.Wpf.Services.Replay;
using Hermes.TradingPlatform.Wpf.ViewModels.Pages;
using Hermes.TradingPlatform.Wpf.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.TradingPlatform.Wpf.Services.Composition;

/// <summary>
/// Composition root for the WPF host. Builds a Microsoft.Extensions.DependencyInjection container
/// and registers the platform graph so view-models can be resolved from a single provider.
/// </summary>
public static class ServiceContainerFactory
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Platform host owns the in-memory event bus, virtual exchange, projections and
        // persistence. We register it as a singleton (one host per process) and expose its
        // public sub-services so individual view-models can request only what they need.
        services.AddSingleton<TradingPlatformHost>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<TradingPlatformHost>().EventBus);
        services.AddSingleton<ITradingStateStore>(sp => sp.GetRequiredService<TradingPlatformHost>().StateStore);
        services.AddSingleton<IVirtualExchange>(sp => sp.GetRequiredService<TradingPlatformHost>().Exchange);
        services.AddSingleton<IRiskValidator>(sp => sp.GetRequiredService<TradingPlatformHost>().RiskValidator);
        services.AddSingleton<TradingReadModel>(sp => sp.GetRequiredService<TradingPlatformHost>().ReadModel);

        // Bridge IPC (file based command/snapshot exchange used by Hermes.TradingPlatform.Cli).
        services.AddSingleton<TradingBridgePublisher>();
        services.AddSingleton<TradingBridgeCommandProcessor>();

        // Page view-models. Replay's service is page-scoped (transient) so reload picks up
        // a fresh journal each time the user navigates back.
        services.AddTransient<JournalReplayService>(sp =>
            new JournalReplayService(
                sp.GetRequiredService<ITradingStateStore>(),
                sp.GetRequiredService<TradingPlatformHost>().JournalStore));

        services.AddSingleton<DashboardViewModel>(sp =>
            new DashboardViewModel(sp.GetRequiredService<TradingReadModel>()));
        services.AddSingleton<PositionsViewModel>(sp =>
            new PositionsViewModel(sp.GetRequiredService<TradingReadModel>(), sp.GetRequiredService<IVirtualExchange>()));
        services.AddSingleton<OrdersViewModel>(sp =>
            new OrdersViewModel(sp.GetRequiredService<TradingReadModel>(), sp.GetRequiredService<IVirtualExchange>()));
        services.AddSingleton<MarketWatchViewModel>(sp =>
            new MarketWatchViewModel(sp.GetRequiredService<TradingReadModel>(), sp.GetRequiredService<IVirtualExchange>()));
        services.AddSingleton<JournalViewModel>(sp =>
            new JournalViewModel(sp.GetRequiredService<TradingReadModel>()));
        services.AddSingleton<HermesViewModel>(sp =>
            new HermesViewModel(sp.GetRequiredService<TradingReadModel>()));
        services.AddSingleton<LogsViewModel>(sp =>
            new LogsViewModel(sp.GetRequiredService<TradingReadModel>(), sp.GetRequiredService<TradingPlatformHost>()));
        services.AddSingleton<RiskManagerViewModel>(sp =>
            new RiskManagerViewModel(sp.GetRequiredService<TradingReadModel>(), sp.GetRequiredService<TradingPlatformHost>()));
        services.AddSingleton<StrategiesViewModel>(sp =>
            new StrategiesViewModel(sp.GetRequiredService<TradingReadModel>(), sp.GetRequiredService<TradingPlatformHost>()));
        services.AddSingleton<AccountSettingsViewModel>(sp =>
            new AccountSettingsViewModel(sp.GetRequiredService<TradingPlatformHost>()));
        services.AddSingleton<SettingsViewModel>(sp =>
            new SettingsViewModel(sp.GetRequiredService<TradingPlatformHost>()));
        services.AddSingleton<ReplayViewModel>(sp =>
            new ReplayViewModel(
                sp.GetRequiredService<JournalReplayService>(),
                sp.GetRequiredService<TradingPlatformHost>().JournalLocation));

        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
