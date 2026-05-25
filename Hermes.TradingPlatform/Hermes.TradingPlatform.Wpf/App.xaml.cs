using System.Windows;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.Services.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.TradingPlatform.Wpf;

public partial class App : Application
{
    /// <summary>
    /// Process-wide DI container. Built once at startup and disposed on exit so any
    /// IDisposable singletons (TradingPlatformHost, bridge IPC, etc.) get cleaned up.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DwmDarkTitleBar.RegisterForAllWindows();
        Services = ServiceContainerFactory.Build();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}
