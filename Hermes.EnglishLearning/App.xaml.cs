using System.Windows;
using Hermes.EnglishLearning.Services;

namespace Hermes.EnglishLearning;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.BeginSession();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Shutdown();
        base.OnExit(e);
    }
}
