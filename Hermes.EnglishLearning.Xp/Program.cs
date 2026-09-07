using System;
using System.Windows.Forms;

namespace Hermes.EnglishLearning.Xp;

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
        AppLog.Info("XP client start BaseDir=" + AppDomain.CurrentDomain.BaseDirectory
            + " version=" + AppVersion.Display
            + " UiScale=" + settings.UiScale.ToString("0.00")
            + " curl=" + (CurlHttp.IsAvailable ? CurlHttp.ExecutablePath : "no"));
        Application.Run(new MainForm(settings));
    }
}
