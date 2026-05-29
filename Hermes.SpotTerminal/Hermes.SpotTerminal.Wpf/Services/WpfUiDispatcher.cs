using System.Windows;
using System.Windows.Threading;

namespace Hermes.SpotTerminal.Wpf.Services;

/// <summary>Marshals callbacks to the WPF UI thread (store/events may fire from exchange threads).</summary>
internal static class WpfUiDispatcher
{
    public static Dispatcher Instance { get; } =
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    public static void Run(Action action)
    {
        var dispatcher = Instance;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
