using System;
using System.Windows.Forms;

namespace Hermes.RemoteTerminal.Xp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = SettingsStore.Load();
        AppLog.StartSession();
        TlsBootstrap.Apply();
        AppLog.Info("RemoteTerminal XP start BaseDir=" + AppDomain.CurrentDomain.BaseDirectory
            + " version=" + AppVersion.Display
            + " curl=" + (CurlHttp.IsAvailable ? CurlHttp.ExecutablePath : "no"));
        Application.Run(new MainForm(settings));
    }
}
