using System.Reflection;

namespace Hermes.Wpf.Services;

public static class AppVersion
{
    public static string Number
    {
        get
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v is null)
                {
                    return "0.0.0";
                }

                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch
            {
                return "0.0.0";
            }
        }
    }

    public static string Config =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>Short: <c>0.1.0 Debug</c>.</summary>
    public static string Display => Number + " " + Config;

    public static string MainWindowTitle => "Hermes Command Center " + Display;

    public static string ChatWindowTitle => "Hermes Chat " + Display;

    public static string LogStamp =>
        "version=" + Number
        + " config=" + Config
        + " tfm=net8.0-windows"
        + " base=" + AppContext.BaseDirectory.TrimEnd('\\', '/');
}
