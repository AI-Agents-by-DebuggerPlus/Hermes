using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace HibernateCountdown;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft = 10;
    private bool _cancelled;

    public MainWindow()
    {
        InitializeComponent();
        CountdownText.Text = _secondsLeft.ToString();
        _timer.Tick += Timer_OnTick;
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _cancelled = true;
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancelled = true;
        _timer.Stop();
        Close();
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        if (_cancelled)
        {
            return;
        }

        _secondsLeft--;
        if (_secondsLeft > 0)
        {
            CountdownText.Text = _secondsLeft.ToString();
            return;
        }

        _timer.Stop();
        CountdownText.Text = "0";
        EnterHibernation();
        Close();
    }

    private static void EnterHibernation()
    {
        try
        {
            // Prefer API; fallback to shutdown /h
            if (!SetSuspendState(hibernate: true, forceCritical: true, disableWakeEvent: true))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/h",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось включить гибернацию:\n{ex.Message}",
                "Hibernate",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [DllImport("Powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
