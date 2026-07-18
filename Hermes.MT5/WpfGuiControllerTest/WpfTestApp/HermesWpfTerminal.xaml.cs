using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

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
        private OrderMode _mode = OrderMode.Market;

        public HermesWpfTerminal()
        {
            InitializeComponent();
            TryRestoreSize();

            btnQuickBuy.Click += (s, e) => LogWpf("Quick BUY clicked, lot=" + txtLot.Text);
            btnQuickSell.Click += (s, e) => LogWpf("Quick SELL clicked, lot=" + txtLot.Text);
            btnBuyMarket.Click += (s, e) => OnSideClick(buy: true);
            btnSellMarket.Click += (s, e) => OnSideClick(buy: false);
            btnPlacePending.Click += (s, e) =>
                LogWpf("Place Order clicked: " + SelectedOrderType() + " vol=" + txtVolume.Text + " price=" + txtPrice.Text);

            tabMarket.Checked += (s, e) => ApplyOrderMode(OrderMode.Market);
            tabLimit.Checked += (s, e) => ApplyOrderMode(OrderMode.Limit);
            tabStop.Checked += (s, e) => ApplyOrderMode(OrderMode.Stop);
            tabStopLimit.Checked += (s, e) => ApplyOrderMode(OrderMode.StopLimit);

            chkAutoTrade.Checked += (s, e) => LogWpf("Auto-trade ON");
            chkAutoTrade.Unchecked += (s, e) => LogWpf("Auto-trade OFF");
            chkRealTrade.Checked += (s, e) => LogWpf("Real trading ON (test only)");
            chkRealTrade.Unchecked += (s, e) => LogWpf("Real trading OFF (test only)");

            txtMqlLog.TextChanged += OnMqlLogFeed;

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
            Loaded += (_, __) =>
            {
                _sizeReady = true;
                DrawOrderHelpDiagram();
            };

            ApplyOrderMode(OrderMode.Market);
            RefreshBidAskBig();
            LogWpf("HermesWpfTerminal ready");
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
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hermes",
                "HermesWpfTerminal");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "window-size.txt");
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
