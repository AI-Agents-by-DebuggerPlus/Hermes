using System.Reflection;

namespace Hermes.RemoteTerminal.Services;

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

    /// <summary>Short: <c>1.2.0 Debug</c>.</summary>
    public static string Display => Number + " " + Config;

    /// <summary>Window title.</summary>
    public static string WindowTitle => "Hermes Remote Terminal " + Display;

    /// <summary>One-line stamp for logs.</summary>
    public static string LogStamp =>
        "version=" + Number
        + " config=" + Config
        + " tfm=net8.0-windows"
        + " base=" + AppContext.BaseDirectory.TrimEnd('\\', '/');
}
