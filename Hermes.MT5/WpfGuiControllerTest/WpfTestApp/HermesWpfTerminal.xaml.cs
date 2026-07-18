using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfTestApp
{
    public partial class HermesWpfTerminal : Window
    {
        private bool _sizeReady;
        private readonly StringBuilder _log = new StringBuilder();
        private SessionsCalendarWindow _calendar;

        public HermesWpfTerminal()
        {
            InitializeComponent();
            TryRestoreSize();
            HighlightMode(btnModeMarket);

            btnQuickBuy.Click += (s, e) => LogWpf("Quick BUY clicked, lot=" + txtLot.Text);
            btnQuickSell.Click += (s, e) => LogWpf("Quick SELL clicked, lot=" + txtLot.Text);
            btnBuyMarket.Click += (s, e) => LogWpf("Buy by Market clicked, vol=" + txtVolume.Text);
            btnSellMarket.Click += (s, e) => LogWpf("Sell by Market clicked, vol=" + txtVolume.Text);
            btnPlacePending.Click += (s, e) =>
                LogWpf("Place Order clicked: " + SelectedOrderType() + " vol=" + txtVolume.Text + " price=" + txtPrice.Text);

            btnModeMarket.Click += (s, e) => { cmbOrderType.SelectedIndex = 0; HighlightMode(btnModeMarket); LogWpf("Mode: Market Execution"); };
            btnModeLimit.Click += (s, e) => { cmbOrderType.SelectedIndex = 1; HighlightMode(btnModeLimit); LogWpf("Mode: Limit Order"); };
            btnModeStop.Click += (s, e) => { cmbOrderType.SelectedIndex = 3; HighlightMode(btnModeStop); LogWpf("Mode: Stop Order"); };
            btnModeStopLimit.Click += (s, e) => { cmbOrderType.SelectedIndex = 5; HighlightMode(btnModeStopLimit); LogWpf("Mode: Stop Limit"); };

            chkAutoTrade.Checked += (s, e) => LogWpf("Auto-trade ON");
            chkAutoTrade.Unchecked += (s, e) => LogWpf("Auto-trade OFF");
            chkRealTrade.Checked += (s, e) => LogWpf("Real trading ON (test only)");
            chkRealTrade.Unchecked += (s, e) => LogWpf("Real trading OFF (test only)");

            txtMqlLog.TextChanged += OnMqlLogFeed;

            // Keep volume fields in sync (local only; MQL5 still listens to txtLot)
            txtLot.TextChanged += (s, e) =>
            {
                if (txtVolume.Text != txtLot.Text)
                    txtVolume.Text = txtLot.Text;
            };
            txtVolume.TextChanged += (s, e) =>
            {
                if (txtLot.Text != txtVolume.Text)
                    txtLot.Text = txtVolume.Text;
            };

            btnSessionsCalendar.Click += (s, e) => OpenSessionsCalendar();

            SizeChanged += (_, __) =>
            {
                if (_sizeReady && WindowState == WindowState.Normal)
                    TrySaveSize();
            };
            Closing += (_, __) =>
            {
                TrySaveSize();
                try { _calendar?.Close(); } catch { /* ignore */ }
            };
            Loaded += (_, __) => _sizeReady = true;

            LogWpf("HermesWpfTerminal ready");
        }

        private string SelectedOrderType()
        {
            if (cmbOrderType.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? "Market";
            return "Market";
        }

        private void HighlightMode(Button active)
        {
            foreach (var b in new[] { btnModeMarket, btnModeLimit, btnModeStop, btnModeStopLimit })
            {
                b.Background = b == active
                    ? (Brush)new SolidColorBrush(Color.FromRgb(0x38, 0x3f, 0x49))
                    : (Brush)new SolidColorBrush(Color.FromRgb(0x2B, 0x31, 0x39));
                b.Foreground = b == active
                    ? (Brush)new SolidColorBrush(Color.FromRgb(0xF8, 0xD1, 0x2F))
                    : (Brush)new SolidColorBrush(Color.FromRgb(0xEA, 0xEC, 0xEF));
            }
        }

        private void OnMqlLogFeed(object sender, TextChangedEventArgs e)
        {
            var line = txtMqlLog.Text;
            if (string.IsNullOrWhiteSpace(line))
                return;
            AppendLogLine(line.Trim());
        }

        private void LogWpf(string text)
        {
            AppendLogLine(DateTime.Now.ToString("HH:mm") + "  [WPF]  " + text);
        }

        private void AppendLogLine(string line)
        {
            if (_log.Length > 0)
                _log.AppendLine();
            _log.Append(line);
            // keep last ~80 lines
            var all = _log.ToString();
            var parts = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (parts.Length > 80)
            {
                _log.Clear();
                _log.Append(string.Join(Environment.NewLine, parts, parts.Length - 80, 80));
            }

            txtLog.Text = _log.ToString();
            logScroll?.ScrollToEnd();
        }

        private void OpenSessionsCalendar()
        {
            if (_calendar != null)
            {
                try
                {
                    if (_calendar.IsLoaded)
                    {
                        _calendar.Activate();
                        return;
                    }
                }
                catch { _calendar = null; }
            }

            _calendar = new SessionsCalendarWindow();
            _calendar.Closed += (_, __) => _calendar = null;
            _calendar.Show();
            LogWpf("Opened sessions calendar");
        }

        private static string SettingsFilePath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hermes",
                "HermesWpfTerminal");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "window-size.txt");
        }

        private void TryRestoreSize()
        {
            try
            {
                var path = SettingsFilePath();
                if (!File.Exists(path))
                    return;
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2)
                    return;
                if (!double.TryParse(lines[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                    return;
                if (!double.TryParse(lines[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                    return;
                if (w >= MinWidth && h >= MinHeight)
                {
                    Width = w;
                    Height = h;
                }
            }
            catch { /* ignore */ }
        }

        private void TrySaveSize()
        {
            try
            {
                if (WindowState != WindowState.Normal)
                    return;
                File.WriteAllText(
                    SettingsFilePath(),
                    Width.ToString("0.##", CultureInfo.InvariantCulture) + Environment.NewLine +
                    Height.ToString("0.##", CultureInfo.InvariantCulture) + Environment.NewLine);
            }
            catch { /* ignore */ }
        }
    }
}
