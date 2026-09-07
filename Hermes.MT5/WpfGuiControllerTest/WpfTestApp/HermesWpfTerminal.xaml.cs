using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WpfTestApp
{
    public partial class HermesWpfTerminal : Window
    {
        private enum OrderMode
        {
            Market,
            Limit,
            Stop,
            StopLimit
        }

        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        private static readonly Brush BlueDotBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
        private static readonly Brush GreenLvl = new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47));
        private static readonly Brush RedLvl = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));

        private bool _sizeReady;
        private readonly StringBuilder _log = new StringBuilder();
        private SessionsCalendarWindow _calendar;
        private SettingsWindow _settings;
        private TradeCommandsTestWindow _tradeCmds;
        private OrderMode _mode = OrderMode.Market;
        private TerminalAgentIpc _agentIpc;
        private string _lastScreenshotPath = "";
        private string _lastScreenshotLink = "";
        private string _lastSymbolsPath = "";
        private bool _tradeFlagsPushedToEa;
        private readonly List<string> _screenshotHistory = new List<string>();
        private ScreenshotViewerWindow _screenshotViewer;

        internal Button BtnQuickBuyPublic => btnQuickBuy;
        internal Button BtnQuickSellPublic => btnQuickSell;
        internal Button BtnCloseAllPublic => btnCloseAllPositions;
        internal Button BtnScreenshotPublic => btnScreenshot;
        internal Button BtnListSymbolsPublic => btnListSymbols;
        internal string LastScreenshotPath => _lastScreenshotPath;
        internal string LastScreenshotLink => _lastScreenshotLink;
        internal string LastSymbolsPath => _lastSymbolsPath;

        public HermesWpfTerminal()
        {
            InitializeComponent();
            ApplyBuildStamp();
            Loaded += (_, __) => ApplyBuildStamp();
            TryRestoreSize();
            LogWpf("Build " + BuildInfo.Version + " / " + BuildInfo.AssemblyFile);

            btnQuickBuy.Click += (s, e) => LogWpf("Quick BUY clicked, lot=" + txtLot.Text);
            btnQuickSell.Click += (s, e) => LogWpf("Quick SELL clicked, lot=" + txtLot.Text);
            btnBuyMarket.Click += (s, e) => OnSideClick(buy: true);
            btnSellMarket.Click += (s, e) => OnSideClick(buy: false);
            btnPlacePending.Click += (s, e) =>
                LogWpf("Place Order clicked: " + SelectedOrderType() + " vol=" + txtVolume.Text + " price=" + txtPrice.Text);

            tabMarket.Checked += (s, e) => { ShowTradingPanel(); ApplyOrderMode(OrderMode.Market); };
            tabLimit.Checked += (s, e) => { ShowTradingPanel(); ApplyOrderMode(OrderMode.Limit); };
            tabStop.Checked += (s, e) => { ShowTradingPanel(); ApplyOrderMode(OrderMode.Stop); };
            tabStopLimit.Checked += (s, e) => { ShowTradingPanel(); ApplyOrderMode(OrderMode.StopLimit); };
            tabScreenshots.Checked += (s, e) => ShowScreenshotsPanel();

            chkAutoTrade.Checked += (s, e) => { LogWpf("Auto-trade ON"); TrySaveTradeSettings(); };
            chkAutoTrade.Unchecked += (s, e) => { LogWpf("Auto-trade OFF"); TrySaveTradeSettings(); };
            chkRealTrade.Checked += (s, e) => { LogWpf("Real trading ON (OrderSend)"); TrySaveTradeSettings(); };
            chkRealTrade.Unchecked += (s, e) => { LogWpf("Real trading OFF (stub ACK)"); TrySaveTradeSettings(); };

            txtMqlLog.TextChanged += OnMqlLogFeed;
            txtScreenshotPath.TextChanged += OnScreenshotPathChanged;
            txtSymbolsPath.TextChanged += OnSymbolsPathChanged;
            btnCaptureScreenshot.Click += (s, e) => RequestChartScreenshot();
            lstScreenshots.SelectionChanged += OnScreenshotHistorySelected;

            var bidAskTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            bidAskTimer.Tick += (_, __) => RefreshBidAskBig();
            bidAskTimer.Start();

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

            btnTradeCommands.Click += (s, e) => OpenTradeCommandsTest();
            btnSessionsCalendar.Click += (s, e) => OpenSessionsCalendar();
            btnSettings.Click += (s, e) => OpenSettings();

            SizeChanged += (_, __) =>
            {
                if (_sizeReady && WindowState == WindowState.Normal)
                    TrySaveSize();
            };
            Closing += (_, __) =>
            {
                TrySaveSize();
                TrySaveTradeSettings();
                try { _agentIpc?.Dispose(); } catch { /* ignore */ }
                _agentIpc = null;
                try { _screenshotViewer?.Close(); } catch { /* ignore */ }
                _screenshotViewer = null;
                try { _calendar?.Close(); } catch { /* ignore */ }
                try { _settings?.Close(); } catch { /* ignore */ }
                try { _tradeCmds?.Close(); } catch { /* ignore */ }
            };
            Loaded += (_, __) =>
            {
                _sizeReady = true;
                TryRestoreTradeSettings();
                DrawOrderHelpDiagram();
                try
                {
                    _agentIpc?.Dispose();
                    _agentIpc = new TerminalAgentIpc(this);
                }
                catch (Exception ex)
                {
                    LogWpf("Agent IPC failed: " + ex.Message);
                }
            };

            ApplyOrderMode(OrderMode.Market);
            ShowTradingPanel();
            RefreshBidAskBig();
            LogWpf("HermesWpfTerminal ready");
        }

        internal void LogFromIpc(string text) => LogWpf(text);

        internal void ClickAgentButton(Button btn)
        {
            if (btn == null)
                throw new InvalidOperationException("button is null");
            btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        internal void BeginScreenshotCapture()
        {
            _lastScreenshotPath = "";
            _lastScreenshotLink = "";
            txtScreenshotPath.Text = "";
            ShowScreenshotsPanel();
            tabScreenshots.IsChecked = true;
            LogWpf("IPC screenshot requested");
        }

        internal void RequestChartScreenshot()
        {
            BeginScreenshotCapture();
            ClickAgentButton(btnScreenshot);
        }

        internal string WaitForScreenshotPath(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!string.IsNullOrWhiteSpace(_lastScreenshotPath))
                    return _lastScreenshotPath;
                DoEvents();
                Thread.Sleep(40);
            }
            return _lastScreenshotPath ?? "";
        }

        internal void BeginSymbolsListCapture()
        {
            _lastSymbolsPath = "";
            txtSymbolsPath.Text = "";
            LogWpf("IPC list_symbols requested");
        }

        internal string WaitForSymbolsPath(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!string.IsNullOrWhiteSpace(_lastSymbolsPath))
                    return _lastSymbolsPath;
                DoEvents();
                Thread.Sleep(40);
            }
            return _lastSymbolsPath ?? "";
        }

        private void OnSymbolsPathChanged(object sender, TextChangedEventArgs e)
        {
            var path = (txtSymbolsPath.Text ?? "").Trim();
            if (path.Length == 0)
                return;

            try
            {
                var dest = System.IO.Path.Combine(TerminalAgentIpc.ResolveIpcDir(), "symbols.json");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest) ?? TerminalAgentIpc.ResolveIpcDir());
                if (!string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path))
                {
                    File.Copy(path, dest, overwrite: true);
                    path = dest;
                }

                _lastSymbolsPath = path;
                LogWpf("Symbols list ready: " + path);
            }
            catch (Exception ex)
            {
                LogWpf("Symbols copy failed: " + ex.Message);
                _lastSymbolsPath = path;
            }
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new DispatcherOperationCallback(arg =>
                {
                    ((DispatcherFrame)arg).Continue = false;
                    return null;
                }),
                frame);
            Dispatcher.PushFrame(frame);
        }

        private void OnScreenshotPathChanged(object sender, TextChangedEventArgs e)
        {
            var path = (txtScreenshotPath.Text ?? "").Trim();
            if (path.Length == 0)
                return;
            ApplyScreenshotFromMt5(path);
        }

        private void ApplyScreenshotFromMt5(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    LogWpf("Screenshot path missing: " + sourcePath);
                    txtScreenshotLink.Text = "Файл не найден: " + sourcePath;
                    return;
                }

                var dest = CopyToProjectScreenshots(sourcePath);
                _lastScreenshotPath = dest;
                _lastScreenshotLink = new Uri(dest).AbsoluteUri;
                txtScreenshotLink.Text = _lastScreenshotLink;
                ShowImage(dest);
                RememberScreenshot(dest);
                ShowScreenshotsPanel();
                tabScreenshots.IsChecked = true;
                // Defer: opening a Window inside WaitForScreenshotPath/DoEvents often fails to activate.
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => OpenFullscreenScreenshot(dest)));
                LogWpf("Screenshot ready: " + dest);
            }
            catch (Exception ex)
            {
                LogWpf("Screenshot display failed: " + ex.Message);
                txtScreenshotLink.Text = "Ошибка: " + ex.Message;
            }
        }

        private static string ResolveScreenshotsDir()
        {
            var env = Environment.GetEnvironmentVariable("HERMES_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();
            return @"D:\Programming\AI_Agents\HermesProjects\Mt5Terminal\hermes\screenshots";
        }

        private string CopyToProjectScreenshots(string sourcePath)
        {
            var dir = ResolveScreenshotsDir();
            Directory.CreateDirectory(dir);
            var name = System.IO.Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(name))
                name = "hermes_chart_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            var dest = System.IO.Path.Combine(dir, name);
            File.Copy(sourcePath, dest, overwrite: true);
            return dest;
        }

        private void ShowImage(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            imgScreenshot.Source = bmp;
        }

        internal void OpenLastScreenshotFullscreen()
        {
            if (string.IsNullOrWhiteSpace(_lastScreenshotPath))
                return;
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => OpenFullscreenScreenshot(_lastScreenshotPath)));
        }

        private void OpenFullscreenScreenshot(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    LogWpf("Fullscreen screenshot skipped — no file: " + (path ?? ""));
                    return;
                }

                // Temporarily lower Topmost on the trading panel so the viewer can stay above.
                var wasTopmost = Topmost;
                Topmost = false;

                if (_screenshotViewer == null || !_screenshotViewer.IsLoaded)
                {
                    _screenshotViewer = new ScreenshotViewerWindow();
                    _screenshotViewer.Closed += (_, __) =>
                    {
                        _screenshotViewer = null;
                        try { Topmost = wasTopmost; } catch { /* ignore */ }
                    };
                    _screenshotViewer.ShowActivated = true;
                    _screenshotViewer.Show();
                }

                _screenshotViewer.ShowImage(path, status =>
                {
                    try
                    {
                        txtScreenshotLink.Text = status;
                    }
                    catch { /* ignore */ }
                });
                if (!_screenshotViewer.IsVisible)
                    _screenshotViewer.Show();
                _screenshotViewer.WindowState = WindowState.Normal;
                _screenshotViewer.ApplyFullscreenPublic();
                _screenshotViewer.Activate();
                _screenshotViewer.Topmost = true;
                _screenshotViewer.Focus();
                LogWpf("Fullscreen screenshot window opened: " + path);
            }
            catch (Exception ex)
            {
                LogWpf("Fullscreen screenshot window failed: " + ex.Message);
            }
        }

        private void RememberScreenshot(string path)
        {
            _screenshotHistory.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            _screenshotHistory.Insert(0, path);
            while (_screenshotHistory.Count > 20)
                _screenshotHistory.RemoveAt(_screenshotHistory.Count - 1);
            lstScreenshots.Items.Clear();
            foreach (var p in _screenshotHistory)
                lstScreenshots.Items.Add(p);
            if (lstScreenshots.Items.Count > 0)
                lstScreenshots.SelectedIndex = 0;
        }

        private void OnScreenshotHistorySelected(object sender, SelectionChangedEventArgs e)
        {
            if (lstScreenshots.SelectedItem is string path && File.Exists(path))
            {
                _lastScreenshotPath = path;
                _lastScreenshotLink = new Uri(path).AbsoluteUri;
                txtScreenshotLink.Text = _lastScreenshotLink;
                ShowImage(path);
                OpenFullscreenScreenshot(path);
            }
        }

        private void ShowTradingPanel()
        {
            panelOrderContent.Visibility = Visibility.Visible;
            panelScreenshots.Visibility = Visibility.Collapsed;
        }

        private void ShowScreenshotsPanel()
        {
            panelOrderContent.Visibility = Visibility.Collapsed;
            panelScreenshots.Visibility = Visibility.Visible;
        }

        internal Button GetCloseSlotButton(int slot)
        {
            switch (slot)
            {
                case 0: return btnClosePos0;
                case 1: return btnClosePos1;
                case 2: return btnClosePos2;
                case 3: return btnClosePos3;
                case 4: return btnClosePos4;
                case 5: return btnClosePos5;
                case 6: return btnClosePos6;
                case 7: return btnClosePos7;
                default: return null;
            }
        }

        internal void ApplyAgentLot(double lot)
        {
            var text = lot.ToString("0.##", CultureInfo.InvariantCulture);
            txtLot.Text = text;
            txtVolume.Text = text;
            LogWpf("IPC set lot=" + text);
        }

        internal void ApplyAgentRealTrading(bool on)
        {
            chkRealTrade.IsChecked = on;
            if (_settings != null)
            {
                try { _settings.RealTrade = on; } catch { /* ignore */ }
            }
            TrySaveTradeSettings();
            LogWpf("IPC Real trading=" + on);
        }

        internal void ApplyAgentAutoTrade(bool on)
        {
            chkAutoTrade.IsChecked = on;
            if (_settings != null)
            {
                try { _settings.AutoTrade = on; } catch { /* ignore */ }
            }
            TrySaveTradeSettings();
            LogWpf("IPC Auto-trade=" + on);
        }

        internal void ApplyAgentPlacePendingOrder(
            string pendingOrderType,
            double price,
            double stopLoss,
            double takeProfit,
            double lot,
            string orderSymbol = null)
        {
            ShowTradingPanel();
            tabMarket.IsChecked = false;
            tabScreenshots.IsChecked = false;

            var type = (pendingOrderType ?? string.Empty).Trim();
            var buy = type.StartsWith("Buy", StringComparison.OrdinalIgnoreCase);
            if (type.IndexOf("Stop Limit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tabStopLimit.IsChecked = true;
                ApplyOrderMode(OrderMode.StopLimit);
            }
            else if (type.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tabStop.IsChecked = true;
                ApplyOrderMode(OrderMode.Stop);
            }
            else
            {
                tabLimit.IsChecked = true;
                ApplyOrderMode(OrderMode.Limit);
            }

            SyncComboForSide(buy);
            if (lot > 0)
            {
                ApplyAgentLot(lot);
            }

            // Explicit label for EA (ComboBox events often send "ComboBoxItem: Buy Limit").
            txtOrderTypeLabel.Text = type;
            txtOrderSymbol.Text = string.IsNullOrWhiteSpace(orderSymbol) ? string.Empty : orderSymbol.Trim();
            txtPrice.Text = price.ToString("G", CultureInfo.InvariantCulture);
            txtSL.Text = stopLoss > 0 ? stopLoss.ToString("G", CultureInfo.InvariantCulture) : "0.000";
            txtTP.Text = takeProfit > 0 ? takeProfit.ToString("G", CultureInfo.InvariantCulture) : "0.000";

            btnPlacePending.Visibility = Visibility.Visible;
            btnBuyMarket.Visibility = Visibility.Collapsed;
            btnSellMarket.Visibility = Visibility.Collapsed;

            LogWpf("IPC place_pending " + type + " sym=" + (txtOrderSymbol.Text.Length > 0 ? txtOrderSymbol.Text : "(chart)")
                   + " lot=" + (lot > 0 ? lot.ToString("G", CultureInfo.InvariantCulture) : txtLot.Text)
                   + " price=" + txtPrice.Text + " SL=" + txtSL.Text + " TP=" + txtTP.Text);
            ClickAgentButton(btnPlacePending);
        }

        internal AgentSnapshot BuildAgentSnapshot(string note)
        {
            var positions = new List<string>();
            CollectPosLine(txtPos0, rowPos0, positions);
            CollectPosLine(txtPos1, rowPos1, positions);
            CollectPosLine(txtPos2, rowPos2, positions);
            CollectPosLine(txtPos3, rowPos3, positions);
            CollectPosLine(txtPos4, rowPos4, positions);
            CollectPosLine(txtPos5, rowPos5, positions);
            CollectPosLine(txtPos6, rowPos6, positions);
            CollectPosLine(txtPos7, rowPos7, positions);

            var logLines = (_log.ToString() ?? "")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Reverse()
                .Take(25)
                .Reverse()
                .ToList();

            return new AgentSnapshot
            {
                utc = DateTime.UtcNow.ToString("o"),
                note = note ?? "",
                build = BuildInfo.Version + " / " + BuildInfo.AssemblyFile,
                ipc_dir = TerminalAgentIpc.ResolveIpcDir(),
                symbol = txtSymbol?.Text ?? "",
                bid = txtBid?.Text ?? "",
                ask = txtAsk?.Text ?? "",
                lot = txtLot?.Text ?? "",
                account = txtAccount?.Text ?? "",
                market_status = txtMarketStatus?.Text ?? "",
                real_trading = chkRealTrade?.IsChecked == true,
                auto_trade = chkAutoTrade?.IsChecked == true,
                positions_header = txtPositionsHeader?.Text ?? "",
                positions = positions,
                log_tail = logLines,
                last_screenshot = _lastScreenshotPath ?? "",
                last_screenshot_link = _lastScreenshotLink ?? ""
            };
        }

        private static void CollectPosLine(TextBlock txt, FrameworkElement row, List<string> sink)
        {
            if (row == null || row.Visibility != Visibility.Visible)
                return;
            var line = (txt?.Text ?? "").Trim();
            if (line.Length > 0)
                sink.Add(line);
        }

        private void OrderDiagramCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawOrderHelpDiagram();
        }

        private void OnSideClick(bool buy)
        {
            SyncComboForSide(buy);
            if (_mode == OrderMode.Market)
                LogWpf((buy ? "Buy by Market" : "Sell by Market") + " clicked, vol=" + txtVolume.Text);
            else
                LogWpf((buy ? "Buy" : "Sell") + " " + _mode + " clicked, vol=" + txtVolume.Text +
                       " price=" + txtPrice.Text +
                       (_mode == OrderMode.StopLimit ? " stopLimit=" + txtStopLimit.Text : ""));
        }

        private void ApplyOrderMode(OrderMode mode)
        {
            _mode = mode;
            bool market = mode == OrderMode.Market;
            bool stopLimit = mode == OrderMode.StopLimit;

            string title;
            switch (mode)
            {
                case OrderMode.Limit: title = "Limit Order"; break;
                case OrderMode.Stop: title = "Stop Order"; break;
                case OrderMode.StopLimit: title = "Stop Limit Order"; break;
                default: title = "Market Execution"; break;
            }
            txtOrderModeTitle.Text = title;

            SetRowVisible(lblPrice, txtPrice, !market);
            SetRowVisible(lblStopLimit, txtStopLimit, stopLimit);
            SetRowVisible(lblFill, cmbFill, market);
            SetRowVisible(lblExpiration, cmbExpiration, !market);

            if (market)
            {
                btnSellMarket.Content = "Sell by Market";
                btnBuyMarket.Content = "Buy by Market";
                cmbOrderType.SelectedIndex = 0;
            }
            else
            {
                btnSellMarket.Content = "Sell";
                btnBuyMarket.Content = "Buy";
                switch (mode)
                {
                    case OrderMode.Limit: cmbOrderType.SelectedIndex = 1; break;
                    case OrderMode.Stop: cmbOrderType.SelectedIndex = 3; break;
                    case OrderMode.StopLimit: cmbOrderType.SelectedIndex = 5; break;
                    default: cmbOrderType.SelectedIndex = 0; break;
                }
            }

            DrawOrderHelpDiagram();
            LogWpf("Mode: " + txtOrderModeTitle.Text);
        }

        private void DrawOrderHelpDiagram()
        {
            if (orderDiagramCanvas == null) return;
            var c = orderDiagramCanvas;
            c.Children.Clear();
            double w = Math.Min(c.ActualWidth > 0 ? c.ActualWidth : c.Width, 300);
            double h = Math.Min(c.ActualHeight > 0 ? c.ActualHeight : c.Height, 220);
            if (w < 80 || h < 80) return;

            switch (_mode)
            {
                case OrderMode.Limit:
                    txtDiagramHint.Text = "Текущая цена — синяя точка. Buy LIMIT: ордер ниже цены, затем цена идёт вверх. Sell LIMIT: ордер выше цены, затем цена идёт вниз.";
                    DrawLimitDiagram(c, w, h);
                    break;
                case OrderMode.Stop:
                    txtDiagramHint.Text = "Текущая цена — синяя точка. Buy STOP: ордер выше цены, цена продолжает вверх. Sell STOP: ордер ниже цены, цена продолжает вниз.";
                    DrawStopDiagram(c, w, h);
                    break;
                case OrderMode.StopLimit:
                    txtDiagramHint.Text = "Stop срабатывает на одном уровне, Limit исполняется на другом. Синяя точка — текущая цена.";
                    DrawStopLimitDiagram(c, w, h);
                    break;
                default:
                    txtDiagramHint.Text = "Market Execution: сделка по текущей рыночной цене (синяя точка).";
                    DrawMarketDiagram(c, w, h);
                    break;
            }
        }

        private static void DrawMarketDiagram(Canvas c, double w, double h)
        {
            double midY = h * 0.55;
            AddLabel(c, "Market", 12, 8, 13, Ink);
            AddDot(c, 40, midY + 40);
            AddPolyArrow(c, new[]
            {
                new Point(40, midY + 40),
                new Point(90, midY + 10),
                new Point(130, midY + 28),
                new Point(w - 36, midY - 50)
            }, Ink, 3);
            AddLabel(c, "исполнение по рынку", 12, h - 28, 11, Muted);
        }

        private static void DrawLimitDiagram(Canvas c, double w, double h)
        {
            AddLabel(c, "Buy LIMIT", 10, 6, 12, GreenLvl);
            AddLabel(c, "Order placed below price → then up", 10, 24, 10, Muted);
            double buyBarY = 70;
            AddLevelBar(c, 70, buyBarY, Math.Max(80, w - 90), true);
            AddDot(c, 50, 42);
            AddPolyArrow(c, new[]
            {
                new Point(50, 42),
                new Point(95, buyBarY + 4),
                new Point(w - 40, 38)
            }, Ink, 2.5);

            double y0 = h * 0.48 + 8;
            AddLabel(c, "Sell LIMIT", 10, y0, 12, RedLvl);
            AddLabel(c, "Order placed above price → then down", 10, y0 + 18, 10, Muted);
            double sellBarY = y0 + 55;
            AddLevelBar(c, 70, sellBarY, Math.Max(80, w - 90), false);
            AddDot(c, 50, sellBarY + 36);
            AddPolyArrow(c, new[]
            {
                new Point(50, sellBarY + 36),
                new Point(95, sellBarY + 4),
                new Point(w - 40, sellBarY + 42)
            }, Ink, 2.5);
        }

        private static void DrawStopDiagram(Canvas c, double w, double h)
        {
            double midX = w * 0.48;
            // Buy STOP: точка ниже уровня → диагональ вверх-вправо (чуть длиннее)
            AddLabel(c, "Buy STOP", 10, 6, 12, GreenLvl);
            AddLabel(c, "above price → keeps going up", 10, 24, 10, Muted);
            double buyBarY = h * 0.30;
            AddLevelLine(c, 24, midX - 14, buyBarY, GreenLvl, 5);
            AddDot(c, 38, h * 0.48);
            AddPolyArrow(c, new[]
            {
                new Point(38, h * 0.48),
                new Point(midX * 0.52, buyBarY),
                new Point(midX - 16, 34)
            }, Ink, 3.2);

            // Sell STOP: точка выше уровня → диагональ вниз-вправо (не по линии Stop)
            AddLabel(c, "Sell STOP", midX + 8, 6, 12, RedLvl);
            AddLabel(c, "below price → keeps going down", midX + 8, 24, 10, Muted);
            double sellBarY = h * 0.30;
            AddLevelLine(c, midX + 16, w - 16, sellBarY, RedLvl, 5);
            AddDot(c, midX + 30, 40);
            AddPolyArrow(c, new[]
            {
                new Point(midX + 30, 40),
                new Point(midX + (w - midX) * 0.48, sellBarY),
                new Point(w - 18, h * 0.50)
            }, Ink, 3.2);

            AddLabel(c, "Текущая цена — синяя точка", 12, h - 26, 11, Muted);
        }

        private static void DrawStopLimitDiagram(Canvas c, double w, double h)
        {
            AddLabel(c, "Buy STOP LIMIT (пример)", 10, 4, 12, Ink);
            double stopY = h * 0.32;
            double limitY = h * 0.58;
            AddLevelLine(c, 40, w - 40, stopY, GreenLvl, 4);
            AddLabel(c, "Stop", w - 70, stopY - 18, 11, GreenLvl);
            AddLevelLine(c, 40, w - 40, limitY, RedLvl, 4);
            AddLabel(c, "Limit", w - 70, limitY - 18, 11, RedLvl);
            AddDot(c, 55, h * 0.78);
            // Финальная стрелка выше линии Stop (как на Screenshot_13)
            double tipY = Math.Max(16, stopY - 36);
            AddPolyArrow(c, new[]
            {
                new Point(55, h * 0.78),
                new Point(110, stopY),
                new Point(160, limitY),
                new Point(w - 40, tipY)
            }, Ink, 2.5);
            AddLabel(c, "цена → Stop → Limit → дальше", 10, h - 26, 11, Muted);
        }

        private static void AddLevelBar(Canvas c, double x, double y, double width, bool buyStyle)
        {
            var top = new Rectangle
            {
                Width = width,
                Height = 7,
                Fill = buyStyle ? GreenLvl : RedLvl,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(top, x);
            Canvas.SetTop(top, y);
            c.Children.Add(top);
            var bot = new Rectangle
            {
                Width = width,
                Height = 7,
                Fill = buyStyle ? RedLvl : GreenLvl,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(bot, x);
            Canvas.SetTop(bot, y + 8);
            c.Children.Add(bot);
        }

        private static void AddLevelLine(Canvas c, double x1, double x2, double y, Brush brush, double thickness)
        {
            c.Children.Add(new Line
            {
                X1 = x1,
                Y1 = y,
                X2 = x2,
                Y2 = y,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }

        private static void AddDot(Canvas c, double cx, double cy)
        {
            var e = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = BlueDotBrush,
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(e, cx - 6);
            Canvas.SetTop(e, cy - 6);
            c.Children.Add(e);
        }

        private static void AddLabel(Canvas c, string text, double x, double y, double size, Brush brush)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = brush,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            c.Children.Add(tb);
        }

        private static void AddPolyArrow(Canvas c, Point[] pts, Brush brush, double thickness)
        {
            if (pts == null || pts.Length < 2) return;
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = pts[0], IsFilled = false };
            for (int i = 1; i < pts.Length; i++)
                fig.Segments.Add(new LineSegment(pts[i], true));
            geo.Figures.Add(fig);
            c.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            });

            var a = pts[pts.Length - 2];
            var b = pts[pts.Length - 1];
            double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double len = 14;
            var p1 = new Point(b.X - len * Math.Cos(ang - 0.45), b.Y - len * Math.Sin(ang - 0.45));
            var p2 = new Point(b.X - len * Math.Cos(ang + 0.45), b.Y - len * Math.Sin(ang + 0.45));
            var head = new PathGeometry();
            var hf = new PathFigure { StartPoint = b, IsClosed = true, IsFilled = true };
            hf.Segments.Add(new LineSegment(p1, true));
            hf.Segments.Add(new LineSegment(p2, true));
            head.Figures.Add(hf);
            c.Children.Add(new System.Windows.Shapes.Path { Data = head, Fill = brush });
        }

        private static void AddVArrow(Canvas c, double x, double yFrom, double yTo, Brush brush, double thickness)
        {
            c.Children.Add(new Line
            {
                X1 = x,
                Y1 = yFrom,
                X2 = x,
                Y2 = yTo,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            bool up = yTo < yFrom;
            double tipY = yTo;
            double wing = 10;
            var head = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(x, tipY), IsClosed = true, IsFilled = true };
            if (up)
            {
                fig.Segments.Add(new LineSegment(new Point(x - wing, tipY + wing * 1.2), true));
                fig.Segments.Add(new LineSegment(new Point(x + wing, tipY + wing * 1.2), true));
            }
            else
            {
                fig.Segments.Add(new LineSegment(new Point(x - wing, tipY - wing * 1.2), true));
                fig.Segments.Add(new LineSegment(new Point(x + wing, tipY - wing * 1.2), true));
            }
            head.Figures.Add(fig);
            c.Children.Add(new System.Windows.Shapes.Path { Data = head, Fill = brush });
        }

        private static void SetRowVisible(FrameworkElement label, FrameworkElement field, bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = v;
            field.Visibility = v;
        }

        private void SyncComboForSide(bool buy)
        {
            switch (_mode)
            {
                case OrderMode.Market:
                    cmbOrderType.SelectedIndex = 0;
                    break;
                case OrderMode.Limit:
                    cmbOrderType.SelectedIndex = buy ? 1 : 2;
                    break;
                case OrderMode.Stop:
                    cmbOrderType.SelectedIndex = buy ? 3 : 4;
                    break;
                case OrderMode.StopLimit:
                    cmbOrderType.SelectedIndex = buy ? 5 : 6;
                    break;
            }
        }

        private string SelectedOrderType()
        {
            if (cmbOrderType.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? "Market";
            return "Market";
        }

        private void RefreshBidAskBig()
        {
            txtBidAskBig.Text = (txtBid.Text ?? "—") + " / " + (txtAsk.Text ?? "—");
        }

        private void OnMqlLogFeed(object sender, TextChangedEventArgs e)
        {
            var line = txtMqlLog.Text;
            if (string.IsNullOrWhiteSpace(line))
                return;
            AppendLogLine(line.Trim());

            // Restore runs before EA is attached; re-push flags once panel is live.
            if (!_tradeFlagsPushedToEa
                && line.IndexOf("panel started", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _tradeFlagsPushedToEa = true;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(PushTradeSettingsToEa));
            }
        }

        /// <summary>
        /// Force GuiController checkbox events so MQL5 gets Real/Auto flags after restart.
        /// </summary>
        private void PushTradeSettingsToEa()
        {
            try
            {
                var real = chkRealTrade?.IsChecked == true;
                var auto = chkAutoTrade?.IsChecked == true;

                // Toggle to guarantee GUI_CHECKBOX_CHANGE reaches EA.
                chkRealTrade.IsChecked = !real;
                chkRealTrade.IsChecked = real;
                chkAutoTrade.IsChecked = !auto;
                chkAutoTrade.IsChecked = auto;

                LogWpf("Pushed settings to EA: Real trading=" + real + " Auto-trade=" + auto);
            }
            catch (Exception ex)
            {
                LogWpf("Push settings to EA failed: " + ex.Message);
            }
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

        private void OpenTradeCommandsTest()
        {
            if (_tradeCmds != null)
            {
                try
                {
                    if (_tradeCmds.IsLoaded)
                    {
                        _tradeCmds.Activate();
                        return;
                    }
                }
                catch { _tradeCmds = null; }
            }

            _tradeCmds = new TradeCommandsTestWindow { Owner = this };
            _tradeCmds.Closed += (_, __) => _tradeCmds = null;
            _tradeCmds.Show();
            LogWpf("Opened trade commands test");
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

        private void OpenSettings()
        {
            if (_settings != null)
            {
                try
                {
                    if (_settings.IsLoaded)
                    {
                        _settings.Activate();
                        return;
                    }
                }
                catch { _settings = null; }
            }

            _settings = new SettingsWindow
            {
                Owner = this,
                AutoTrade = chkAutoTrade.IsChecked == true,
                RealTrade = chkRealTrade.IsChecked == true
            };
            _settings.WireEvents();
            _settings.AutoTradeChanged += () =>
            {
                chkAutoTrade.IsChecked = _settings.AutoTrade;
                TrySaveTradeSettings();
            };
            _settings.RealTradeChanged += () =>
            {
                chkRealTrade.IsChecked = _settings.RealTrade;
                TrySaveTradeSettings();
            };
            _settings.Closed += (_, __) =>
            {
                _settings = null;
                NotifySettingsClosed();
            };
            _settings.Show();
            LogWpf("Opened settings");
        }

        /// <summary>
        /// Сигнал в очередь GuiController → MQL5 Experts (скрытая кнопка).
        /// </summary>
        private void NotifySettingsClosed()
        {
            try
            {
                btnSettingsClosed.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            catch { /* ignore */ }
            LogWpf("Settings closed");
        }

        private static string SettingsFilePath()
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hermes",
                "HermesWpfTerminal");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "window-size.txt");
        }

        private static string TradeSettingsFilePath()
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hermes",
                "HermesWpfTerminal");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "trading-settings.txt");
        }

        private void TryRestoreTradeSettings()
        {
            try
            {
                var path = TradeSettingsFilePath();
                if (!File.Exists(path))
                    return;

                var real = false;
                var auto = false;
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = (line ?? string.Empty).Trim();
                    if (t.Length == 0 || t.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    var eq = t.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    var key = t.Substring(0, eq).Trim();
                    var val = t.Substring(eq + 1).Trim();
                    var on = val.Equals("1", StringComparison.OrdinalIgnoreCase)
                             || val.Equals("true", StringComparison.OrdinalIgnoreCase)
                             || val.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    if (key.Equals("RealTrade", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("RealTrading", StringComparison.OrdinalIgnoreCase))
                        real = on;
                    else if (key.Equals("AutoTrade", StringComparison.OrdinalIgnoreCase))
                        auto = on;
                }

                chkRealTrade.IsChecked = real;
                chkAutoTrade.IsChecked = auto;
                LogWpf("Restored settings: Real trading=" + real + " Auto-trade=" + auto);
            }
            catch (Exception ex)
            {
                LogWpf("Restore settings failed: " + ex.Message);
            }
        }

        private void TrySaveTradeSettings()
        {
            try
            {
                var real = chkRealTrade?.IsChecked == true;
                var auto = chkAutoTrade?.IsChecked == true;
                File.WriteAllText(
                    TradeSettingsFilePath(),
                    "RealTrade=" + (real ? "1" : "0") + Environment.NewLine +
                    "AutoTrade=" + (auto ? "1" : "0") + Environment.NewLine);
            }
            catch { /* ignore */ }
        }

        private void ApplyBuildStamp()
        {
            Title = BuildInfo.MainWindowTitle;
            if (txtBuildVersion != null)
                txtBuildVersion.Text = BuildInfo.Version;
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
