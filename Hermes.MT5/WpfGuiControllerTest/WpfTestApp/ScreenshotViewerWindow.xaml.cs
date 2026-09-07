using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WpfTestApp
{
    public partial class ScreenshotViewerWindow : Window
    {
        public const int AutoBlankSeconds = 10;

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private int _secondsLeft;
        private Action<string> _statusCallback;
        private ImageSource _lastImage;
        private string _lastLabel = "";
        private bool _asleep;

        public bool HasLastScreenshot { get { return _lastImage != null; } }
        public bool IsAsleep { get { return _asleep; } }

        public ScreenshotViewerWindow()
        {
            InitializeComponent();
            _timer.Tick += Timer_OnTick;
            Closed += (_, __) =>
            {
                _timer.Stop();
                Mouse.OverrideCursor = null;
                if (_statusCallback != null)
                    _statusCallback("Screenshot closed");
                _statusCallback = null;
            };
        }

        public void ShowImage(string path, Action<string> statusCallback = null)
        {
            _statusCallback = statusCallback;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                txtPath.Text = "Файл не найден: " + (path ?? "");
                imgView.Source = null;
                _lastImage = null;
                return;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            _lastImage = bmp;
            _lastLabel = path;
            ApplyLastImageVisible();
            Title = "MT5 Screenshot — " + Path.GetFileName(path);
            ApplyFullscreen();
            _asleep = false;
            SystemDisplayPower.TurnOnMonitors();
            StartCountdown();
            Activate();
            Focus();
            Keyboard.Focus(this);
        }

        public void ApplyFullscreenPublic() => ApplyFullscreen();

        public bool TryWakeWithSpace()
        {
            if (!_asleep || _lastImage == null)
                return false;
            WakeAndShowAgain();
            return true;
        }

        private void ApplyLastImageVisible()
        {
            imgView.Source = _lastImage;
            imgView.Visibility = Visibility.Visible;
            if (blankOverlay != null)
                blankOverlay.Visibility = Visibility.Collapsed;
            if (chromeBar != null)
                chromeBar.Visibility = Visibility.Visible;
            Mouse.OverrideCursor = null;
            txtPath.Text = _lastLabel;
        }

        private void StartCountdown()
        {
            _timer.Stop();
            _secondsLeft = AutoBlankSeconds;
            UpdateCountdownUi();
            _timer.Start();
        }

        private void Timer_OnTick(object sender, EventArgs e)
        {
            _secondsLeft--;
            if (_secondsLeft <= 0)
            {
                _timer.Stop();
                EnterSleepKeepScreenshot();
                return;
            }

            UpdateCountdownUi();
        }

        private void UpdateCountdownUi()
        {
            var msg = "Screenshot · мониторы через " + _secondsLeft + " с";
            if (txtCountdown != null)
                txtCountdown.Text = msg;
            if (_statusCallback != null)
                _statusCallback(msg);
        }

        private void EnterSleepKeepScreenshot()
        {
            if (chromeBar != null)
                chromeBar.Visibility = Visibility.Collapsed;
            if (blankOverlay != null)
                blankOverlay.Visibility = Visibility.Visible;
            imgView.Visibility = Visibility.Visible;
            _asleep = true;
            Mouse.OverrideCursor = Cursors.None;
            if (_statusCallback != null)
                _statusCallback("Screenshot · мониторы выкл · Пробел = ещё 10 с");

            // Do not Activate() here — it often prevents SC_MONITORPOWER from taking effect.
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    if (!_asleep)
                        return;
                    SystemDisplayPower.TurnOffMonitors();
                }));
        }

        private void WakeAndShowAgain()
        {
            _asleep = false;
            SystemDisplayPower.TurnOnMonitors();
            ApplyFullscreen();
            ApplyLastImageVisible();
            StartCountdown();
            Activate();
            Focus();
            Keyboard.Focus(this);
            if (_statusCallback != null)
                _statusCallback("Screenshot · пробуждение · ещё 10 с");
        }

        private void Window_OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyFullscreen();
            Activate();
            Focus();
        }

        private void ApplyFullscreen()
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            Topmost = true;
            ShowInTaskbar = true;
        }

        private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

        private void Window_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                if (_asleep)
                    WakeAndShowAgain();
                return;
            }

            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                e.Handled = true;
                Close();
            }
        }

        private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == btnClose || IsDescendantOf(e.OriginalSource as DependencyObject, btnClose))
                return;
            if (_asleep)
            {
                e.Handled = true;
                return;
            }

            Close();
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            while (child != null)
            {
                if (ReferenceEquals(child, ancestor))
                    return true;
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }

            return false;
        }
    }
}
