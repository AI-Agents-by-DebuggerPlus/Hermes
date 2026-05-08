using System.Windows;
using DesktopVoiceChat.Services;

namespace DesktopVoiceChat;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLogService.Initialize();
        AppLogService.Log("Приложение запущено.", "App");
        base.OnStartup(e);
    }
}

