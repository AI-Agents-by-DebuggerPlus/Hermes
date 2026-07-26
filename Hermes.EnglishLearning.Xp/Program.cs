using System;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace Hermes.EnglishLearning.Xp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            // Supabase needs TLS 1.2. On XP SP3 install Hotfix / Easy Fix for TLS 1.2 first.
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)192;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 4;
        }
        catch
        {
            // Older runtimes may not accept Tls12 enum cast — leave default.
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = SettingsStore.Load();
        AppLog.StartSession();
        AppLog.Info("XP client start BaseDir=" + AppDomain.CurrentDomain.BaseDirectory
            + " OS=" + Environment.OSVersion + " UiScale=" + settings.UiScale.ToString("0.00"));
        Application.Run(new MainForm(settings));
    }
}
