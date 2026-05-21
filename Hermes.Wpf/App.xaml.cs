using System.Windows;
using Hermes.Wpf.Services;

namespace Hermes.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DwmDarkTitleBar.RegisterForAllWindows();
        base.OnStartup(e);
    }
}

