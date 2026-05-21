using System.Windows;
using System.Windows.Threading;

namespace Hermes.TradingPlatform.Wpf.Threading;

internal static class WpfThreading
{
    public static void RunOnUi(Action action, DispatcherPriority priority = DispatcherPriority.DataBind)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(priority, action);
    }
}
