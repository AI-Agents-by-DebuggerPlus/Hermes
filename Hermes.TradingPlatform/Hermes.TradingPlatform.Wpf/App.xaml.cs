using System.Windows;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DwmDarkTitleBar.RegisterForAllWindows();
        base.OnStartup(e);
    }
}
