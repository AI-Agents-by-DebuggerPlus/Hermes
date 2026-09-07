using System.Media;
using System.Windows;
using System.Windows.Threading;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class HermesErrorHelpWindow : Window
{
    private static HermesErrorHelpWindow? _current;

    public HermesErrorHelpWindow(HermesCliErrorHelp help)
    {
        InitializeComponent();
        TitleText.Text = help.Title;
        RawErrorBox.Text = help.RawError;
        ExplanationBox.Text = help.Explanation;
        FixBox.Text = help.FixInstructions;
        Title = help.Title;
    }

    public static void ShowForError(Window? owner, string? rawError, int? exitCode = null)
    {
        var help = HermesCliErrorExplainer.Explain(rawError, exitCode);

        void Show()
        {
            try { SystemSounds.Hand.Play(); }
            catch { /* ignore */ }

            try { _current?.Close(); }
            catch { /* ignore */ }

            var win = new HermesErrorHelpWindow(help);
            _current = win;
            win.Closed += (_, _) =>
            {
                if (ReferenceEquals(_current, win))
                    _current = null;
            };

            if (owner is { IsVisible: true })
            {
                win.Owner = owner;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            win.Show();
            try { win.Activate(); }
            catch { /* ignore */ }
        }

        var app = Application.Current;
        if (app?.Dispatcher is null)
        {
            Show();
            return;
        }

        if (app.Dispatcher.CheckAccess())
            Show();
        else
            app.Dispatcher.Invoke(Show, DispatcherPriority.Normal);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
