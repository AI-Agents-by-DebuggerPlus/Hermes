using System.Windows;
using Hermes.SpotTerminal.Wpf.Bridge;
using Hermes.SpotTerminal.Wpf.Services;
using Hermes.SpotTerminal.Wpf.Services.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.SpotTerminal.Wpf;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        Services = ServiceContainerFactory.Build();
        // Eager-start bridge services (command queue + snapshot heartbeat).
        _ = Services.GetRequiredService<SpotTerminalFileLogSink>();
        _ = Services.GetRequiredService<SpotBridgePublisher>();
        _ = Services.GetRequiredService<SpotBridgeCommandProcessor>();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is IDisposable d)
        {
            d.Dispose();
        }

        base.OnExit(e);
    }
}
