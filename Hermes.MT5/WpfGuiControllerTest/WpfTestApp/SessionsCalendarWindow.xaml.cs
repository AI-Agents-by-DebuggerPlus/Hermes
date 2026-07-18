using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WpfTestApp
{
    public partial class SessionsCalendarWindow : Window
    {
        private static readonly TimeZoneInfo LocalTz = TimeZoneInfo.Local;
        private const double ViewHours = 24; // total span; "now" is always at the center

        // Approximate major FX session hours in UTC.
        private static readonly SessionDef[] Sessions =
        {
            new SessionDef("Sydney",   21, 0,  6, 0, "#2E7D32", true),
            new SessionDef("Tokyo",     0, 0,  9, 0, "#C62828", false),
            new SessionDef("London",    7, 0, 16, 0, "#F0B90B", true),
            new SessionDef("New York", 12, 0, 21, 0, "#1565C0", false),
        };

        private readonly DispatcherTimer _timer;

        public SessionsCalendarWindow()
        {
            InitializeComponent();
            Refresh();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timer.Tick += (_, __) => Refresh();
            _timer.Start();
            Closed += (_, __) => _timer.Stop();
        }

        private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawTimeline();
        }

        private void Refresh()
        {
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, LocalTz);
            var offset = LocalTz.GetUtcOffset(nowUtc);
            int offsetH = (int)offset.TotalHours;
            int offsetM = Math.Abs(offset.Minutes);
            string offsetLabel = "UTC" + (offset >= TimeSpan.Zero ? "+" : "−") +
                                 Math.Abs(offsetH).ToString("00") +
                                 (offsetM != 0 ? ":" + offsetM.ToString("00") : "");

            Title = "Trading Sessions · local (" + offsetLabel + ")";
            txtHeader.Text = "FX sessions · " + LocalTz.DisplayName;
            txtNow.Text = "Сейчас: " + nowLocal.ToString("yyyy.MM.dd HH:mm") + "  (" + offsetLabel + ")";
            txtActive.Text = BuildActiveLine(nowUtc);

            var sb = new StringBuilder();
            sb.AppendLine("Сессии в местном времени (сегодня):");
            foreach (var s in Sessions)
            {
                var segs = LocalSegmentsForDay(s, nowLocal.Date);
                sb.Append("  ").Append(s.Name).Append(": ");
                for (int i = 0; i < segs.Count; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    sb.Append(FormatHm(segs[i].Item1)).Append("–").Append(FormatHm(segs[i].Item2));
                }
                sb.AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("UTC (для справки): Sydney 21:00–06:00 · Tokyo 00:00–09:00 · London 07:00–16:00 · NY 12:00–21:00");
            sb.AppendLine("Шкала: ±12 ч от текущего времени (сейчас по центру).");
            sb.AppendLine("Яркие полосы — рынок открыт; тусклые — FX-выходные (пт 22:00 UTC → вс 22:00 UTC).");
            txtDetails.Text = sb.ToString();

            DrawTimeline();
        }

        private void DrawTimeline()
        {
            var canvas = timelineCanvas;
            canvas.Children.Clear();
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 40 || h < 40) return;

            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, LocalTz);
            // Floor to minute so labels stay stable between refreshes
            nowLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day,
                nowLocal.Hour, nowLocal.Minute, 0, DateTimeKind.Unspecified);
            var viewStart = nowLocal.AddHours(-ViewHours / 2.0);
            var viewEnd = nowLocal.AddHours(ViewHours / 2.0);
            double viewSpanMin = ViewHours * 60.0;

            const double padL = 8, padR = 8, padTop = 28;
            double plotW = w - padL - padR;
            double laneH = 28;
            double topLaneY = padTop + 8;
            double botLaneY = topLaneY + laneH + 10;
            // Leave room under bars for marker time + session name, then hour ticks.
            const double markerLabelBlock = 30;
            double axisY = botLaneY + laneH + markerLabelBlock;

            Func<DateTime, double> toX = t =>
            {
                double m = (t - viewStart).TotalMinutes;
                return padL + plotW * (m / viewSpanMin);
            };

            // Background track
            canvas.Children.Add(new Rectangle
            {
                Width = plotW,
                Height = botLaneY + laneH - topLaneY + 8,
                Fill = new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1A)),
                RadiusX = 2,
                RadiusY = 2
            });
            Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], padL);
            Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], topLaneY - 4);

            // Hour ticks aligned to local clock hours inside the view
            var tick = viewStart.AddMinutes(-viewStart.Minute).AddHours(1);
            if (viewStart.Minute == 0) tick = viewStart;
            else tick = new DateTime(viewStart.Year, viewStart.Month, viewStart.Day,
                viewStart.Hour, 0, 0).AddHours(1);

            for (var t = tick; t <= viewEnd; t = t.AddHours(1))
            {
                if (t.Hour % 3 != 0) continue;
                double x = toX(t);
                canvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = axisY, Y2 = axisY + 5,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C)),
                    StrokeThickness = 1
                });
                var lbl = new TextBlock
                {
                    Text = t.ToString("HH:mm"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C))
                };
                canvas.Children.Add(lbl);
                Canvas.SetLeft(lbl, Math.Max(0, Math.Min(w - 36, x - 14)));
                Canvas.SetTop(lbl, axisY + 6);
            }

            // Session bars in the centered window
            foreach (var s in Sessions)
            {
                var bright = ParseColor(s.ColorHex);
                var muted = MuteColor(bright);
                double y = s.TopLane ? topLaneY : botLaneY;
                foreach (var seg in SessionIntervalsInView(s, viewStart, viewEnd))
                {
                    DrawBarSplitByMarketAbs(canvas, toX, y, laneH, seg.Item1, seg.Item2, bright, muted);
                }
            }

            // Session-start markers visible in the view
            foreach (var m in CollectMarkersInView(viewStart, viewEnd))
            {
                double x = toX(m.When);
                canvas.Children.Add(new Line
                {
                    X1 = x, X2 = x,
                    Y1 = topLaneY - 2,
                    Y2 = botLaneY + laneH + 2,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0xEA, 0xEC, 0xEF)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                });

                var timeLbl = new TextBlock
                {
                    Text = m.When.ToString("HH:mm"),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                };
                canvas.Children.Add(timeLbl);
                Canvas.SetLeft(timeLbl, Math.Max(0, Math.Min(w - 40, x - 16)));

                // Time on top of the pair; session name always below the time (no overlap).
                double timeY = m.LabelAbove ? 2 : botLaneY + laneH + 2;
                Canvas.SetTop(timeLbl, timeY);

                if (!string.IsNullOrEmpty(m.Name))
                {
                    var nameLbl = new TextBlock
                    {
                        Text = m.Name,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White
                    };
                    canvas.Children.Add(nameLbl);
                    Canvas.SetLeft(nameLbl, Math.Max(0, Math.Min(w - 70, x - 16)));
                    Canvas.SetTop(nameLbl, timeY + 14);
                }
            }

            // Now always at the center
            double nx = padL + plotW * 0.5;
            canvas.Children.Add(new Line
            {
                X1 = nx, X2 = nx,
                Y1 = topLaneY - 6,
                Y2 = botLaneY + laneH + 6,
                Stroke = new SolidColorBrush(Color.FromRgb(0xF8, 0xD1, 0x2F)),
                StrokeThickness = 2
            });
            var nowCap = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(0xF8, 0xD1, 0x2F))
            };
            canvas.Children.Add(nowCap);
            Canvas.SetLeft(nowCap, nx - 4);
            Canvas.SetTop(nowCap, topLaneY - 10);

            var nowLbl = new TextBlock
            {
                Text = "сейчас",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xD1, 0x2F))
            };
            canvas.Children.Add(nowLbl);
            Canvas.SetLeft(nowLbl, nx + 6);
            Canvas.SetTop(nowLbl, topLaneY - 12);
        }

        private static void DrawBarSplitByMarketAbs(Canvas canvas, Func<DateTime, double> toX,
            double y, double h, DateTime fromLocal, DateTime toLocal, Color bright, Color muted)
        {
            if (toLocal <= fromLocal) return;
            var cursor = fromLocal;
            int guard = 0;
            while (cursor < toLocal && guard++ < 48)
            {
                var utcAt = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(cursor, DateTimeKind.Unspecified), LocalTz);
                bool open = !IsFxWeekendUtc(utcAt);
                var nextUtc = NextFxMarketStateChangeUtc(utcAt);
                var nextLocal = TimeZoneInfo.ConvertTimeFromUtc(nextUtc, LocalTz);
                if (nextLocal <= cursor)
                    nextLocal = cursor.AddMinutes(30);
                var end = nextLocal < toLocal ? nextLocal : toLocal;
                DrawBarAbs(canvas, toX, y, h, cursor, end,
                    new SolidColorBrush(open ? bright : muted),
                    open ? 0.95 : 0.55);
                cursor = end;
            }
        }

        private static void DrawBarAbs(Canvas canvas, Func<DateTime, double> toX,
            double y, double h, DateTime from, DateTime to, Brush brush, double opacity)
        {
            if (to <= from) return;
            double x1 = toX(from);
            double x2 = toX(to);
            double width = x2 - x1;
            if (width < 1) width = 1;
            var bar = new Rectangle
            {
                Width = width,
                Height = h,
                Fill = brush,
                RadiusX = 2,
                RadiusY = 2,
                Opacity = opacity
            };
            canvas.Children.Add(bar);
            Canvas.SetLeft(bar, x1);
            Canvas.SetTop(bar, y);
        }

        /// <summary>Session intervals overlapping [viewStart, viewEnd] in local wall time.</summary>
        private static List<Tuple<DateTime, DateTime>> SessionIntervalsInView(
            SessionDef s, DateTime viewStart, DateTime viewEnd)
        {
            var result = new List<Tuple<DateTime, DateTime>>();
            // Cover UTC days that can intersect the local view window
            var utcProbeStart = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(viewStart.AddDays(-1), DateTimeKind.Unspecified), LocalTz).Date;
            var utcProbeEnd = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(viewEnd.AddDays(1), DateTimeKind.Unspecified), LocalTz).Date;

            for (var day = utcProbeStart; day <= utcProbeEnd; day = day.AddDays(1))
            {
                var startUtc = new DateTime(day.Year, day.Month, day.Day, s.UtcFromH, s.UtcFromM, 0, DateTimeKind.Utc);
                var endUtc = new DateTime(day.Year, day.Month, day.Day, s.UtcToH, s.UtcToM, 0, DateTimeKind.Utc);
                if (endUtc <= startUtc)
                    endUtc = endUtc.AddDays(1);

                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, LocalTz);
                var endLocal = TimeZoneInfo.ConvertTimeFromUtc(endUtc, LocalTz);
                if (endLocal <= viewStart || startLocal >= viewEnd)
                    continue;

                var a = startLocal < viewStart ? viewStart : startLocal;
                var b = endLocal > viewEnd ? viewEnd : endLocal;
                if (b > a)
                    result.Add(Tuple.Create(a, b));
            }

            return result;
        }

        private static List<Tuple<double, double>> LocalSegmentsForDay(SessionDef s, DateTime localDay)
        {
            var dayStart = localDay.Date;
            var dayEnd = dayStart.AddDays(1);
            var result = new List<Tuple<double, double>>();
            foreach (var seg in SessionIntervalsInView(s, dayStart, dayEnd))
            {
                double a = (seg.Item1 - dayStart).TotalMinutes;
                double b = seg.Item2 >= dayEnd ? 24 * 60 : (seg.Item2 - dayStart).TotalMinutes;
                if (b > a)
                    result.Add(Tuple.Create(a, b));
            }
            return MergeSegments(result);
        }

        private static List<Tuple<double, double>> MergeSegments(List<Tuple<double, double>> segs)
        {
            if (segs.Count <= 1) return segs;
            segs.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            var merged = new List<Tuple<double, double>>();
            double a = segs[0].Item1, b = segs[0].Item2;
            for (int i = 1; i < segs.Count; i++)
            {
                if (segs[i].Item1 <= b + 0.5)
                    b = Math.Max(b, segs[i].Item2);
                else
                {
                    merged.Add(Tuple.Create(a, b));
                    a = segs[i].Item1;
                    b = segs[i].Item2;
                }
            }
            merged.Add(Tuple.Create(a, b));
            return merged;
        }

        private static List<Marker> CollectMarkersInView(DateTime viewStart, DateTime viewEnd)
        {
            var list = new List<Marker>();
            var utcProbeStart = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(viewStart.AddDays(-1), DateTimeKind.Unspecified), LocalTz).Date;
            var utcProbeEnd = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(viewEnd.AddDays(1), DateTimeKind.Unspecified), LocalTz).Date;

            foreach (var s in Sessions)
            {
                for (var day = utcProbeStart; day <= utcProbeEnd; day = day.AddDays(1))
                {
                    var openUtc = new DateTime(day.Year, day.Month, day.Day, s.UtcFromH, s.UtcFromM, 0, DateTimeKind.Utc);
                    var openLocal = TimeZoneInfo.ConvertTimeFromUtc(openUtc, LocalTz);
                    if (openLocal > viewStart && openLocal < viewEnd)
                    {
                        list.Add(new Marker
                        {
                            When = openLocal,
                            Name = s.Name,
                            LabelAbove = s.TopLane
                        });
                    }
                }
            }

            list.Sort((a, b) => a.When.CompareTo(b.When));
            var dedup = new List<Marker>();
            foreach (var m in list)
            {
                if (dedup.Count > 0 && Math.Abs((dedup[dedup.Count - 1].When - m.When).TotalMinutes) < 20)
                    continue;
                dedup.Add(m);
            }
            return dedup;
        }

        private static DateTime NextFxMarketStateChangeUtc(DateTime utc)
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            if (IsFxWeekendUtc(utc))
            {
                int daysUntilSun = ((int)DayOfWeek.Sunday - (int)utc.DayOfWeek + 7) % 7;
                var sun = utc.Date.AddDays(daysUntilSun).AddHours(22);
                if (sun <= utc)
                    sun = sun.AddDays(7);
                return sun;
            }

            int daysUntilFri = ((int)DayOfWeek.Friday - (int)utc.DayOfWeek + 7) % 7;
            var fri = utc.Date.AddDays(daysUntilFri).AddHours(22);
            if (fri <= utc)
                fri = fri.AddDays(7);
            return fri;
        }

        private static Color ParseColor(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static Color MuteColor(Color c)
        {
            const double keep = 0.28;
            const byte grey = 0x3A;
            return Color.FromRgb(
                (byte)(grey + (c.R - grey) * keep),
                (byte)(grey + (c.G - grey) * keep),
                (byte)(grey + (c.B - grey) * keep));
        }

        private static string FormatHm(double minutes)
        {
            int m = ((int)Math.Round(minutes) + 24 * 60) % (24 * 60);
            int hh = m / 60;
            int mm = m % 60;
            return hh.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   mm.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string BuildActiveLine(DateTime utc)
        {
            string clock = ActiveNow(utc);
            if (IsFxWeekendUtc(utc))
            {
                return "По часам: " + clock +
                       " — но FX рынок закрыт (выходные). Котировки не обновляются до вс ~22:00 UTC.";
            }
            return "Активно сейчас: " + clock;
        }

        private static bool IsFxWeekendUtc(DateTime utc)
        {
            int mins = utc.Hour * 60 + utc.Minute;
            if (utc.DayOfWeek == DayOfWeek.Saturday) return true;
            if (utc.DayOfWeek == DayOfWeek.Sunday && mins < 22 * 60) return true;
            if (utc.DayOfWeek == DayOfWeek.Friday && mins >= 22 * 60) return true;
            return false;
        }

        private static string ActiveNow(DateTime utc)
        {
            int mins = utc.Hour * 60 + utc.Minute;
            var parts = new List<string>();
            if (mins >= 21 * 60 || mins < 6 * 60) parts.Add("Sydney");
            if (mins < 9 * 60) parts.Add("Tokyo");
            if (mins >= 7 * 60 && mins < 16 * 60) parts.Add("London");
            if (mins >= 12 * 60 && mins < 21 * 60) parts.Add("New York");
            return parts.Count == 0 ? "Off-hours" : string.Join("/", parts);
        }

        private sealed class SessionDef
        {
            public string Name;
            public int UtcFromH, UtcFromM, UtcToH, UtcToM;
            public string ColorHex;
            public bool TopLane;

            public SessionDef(string name, int fromH, int fromM, int toH, int toM, string color, bool top)
            {
                Name = name;
                UtcFromH = fromH; UtcFromM = fromM;
                UtcToH = toH; UtcToM = toM;
                ColorHex = color;
                TopLane = top;
            }
        }

        private sealed class Marker
        {
            public DateTime When;
            public string Name;
            public bool LabelAbove;
        }
    }
}
