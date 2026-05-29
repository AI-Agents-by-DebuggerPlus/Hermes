using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Events;
using Hermes.SpotTerminal.Wpf.Bridge;
using Hermes.SpotTerminal.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.SpotTerminal.Wpf.Services.Composition;

public static class ServiceContainerFactory
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SpotTerminalHost>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<SpotTerminalHost>().EventBus);
        services.AddSingleton<ISpotStateStore>(sp => sp.GetRequiredService<SpotTerminalHost>().StateStore);
        services.AddSingleton<SpotReadModel>(sp => sp.GetRequiredService<SpotTerminalHost>().ReadModel);
        services.AddSingleton<SpotBridgePublisher>();
        services.AddSingleton<SpotBridgeCommandProcessor>();
        services.AddSingleton<SpotTerminalFileLogSink>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }
}
