using System.Windows;
using Hermes.RemoteTerminal.Services;

namespace Hermes.RemoteTerminal;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.StartSession();
        base.OnStartup(e);
    }
}
