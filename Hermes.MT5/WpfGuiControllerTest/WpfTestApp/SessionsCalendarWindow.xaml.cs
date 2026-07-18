using System;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace WpfTestApp
{
    public partial class SessionsCalendarWindow : Window
    {
        private readonly DispatcherTimer _timer;

        public SessionsCalendarWindow()
        {
            InitializeComponent();
            Refresh();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += (_, __) => Refresh();
            _timer.Start();
            Closed += (_, __) => _timer.Stop();
        }

        private void Refresh()
        {
            // Fixed UTC hours → display as PT (UTC−8)
            // Sydney 21-06 UTC, Tokyo 00-09, London 07-16, New York 12-21
            var sb = new StringBuilder();
            var nowUtc = DateTime.UtcNow;
            var nowPt = nowUtc.AddHours(-8);

            sb.AppendLine("Now PT: " + nowPt.ToString("yyyy.MM.dd HH:mm") + "  |  UTC: " + nowUtc.ToString("HH:mm"));
            sb.AppendLine();
            sb.AppendLine("Session          UTC            Pacific (UTC−8)");
            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine(FormatRow("Sydney", 21, 6));
            sb.AppendLine(FormatRow("Tokyo", 0, 9));
            sb.AppendLine(FormatRow("London", 7, 16));
            sb.AppendLine(FormatRow("New York", 12, 21));
            sb.AppendLine();
            sb.AppendLine("Active now: " + ActiveNow(nowUtc));
            sb.AppendLine();
            sb.AppendLine("Typical weekly FX open:  Sunday 22:00 UTC = Sunday 14:00 PT (Sydney)");
            sb.AppendLine("Typical weekly FX close: Friday 22:00 UTC = Friday 14:00 PT");
            sb.AppendLine();
            sb.AppendLine("Next 48h (PT):");
            AppendNext48h(sb, nowUtc);

            txtCalendar.Text = sb.ToString();
        }

        private static string FormatRow(string name, int utcFrom, int utcTo)
        {
            int ptFrom = (utcFrom - 8 + 24) % 24;
            int ptTo = (utcTo - 8 + 24) % 24;
            return string.Format("{0,-14}  {1:00}:00–{2:00}:00   {3:00}:00–{4:00}:00",
                name, utcFrom, utcTo, ptFrom, ptTo);
        }

        private static string ActiveNow(DateTime utc)
        {
            int m = utc.Hour * 60 + utc.Minute;
            var parts = new System.Collections.Generic.List<string>();
            if (m >= 21 * 60 || m < 6 * 60) parts.Add("Sydney");
            if (m < 9 * 60) parts.Add("Tokyo");
            if (m >= 7 * 60 && m < 16 * 60) parts.Add("London");
            if (m >= 12 * 60 && m < 21 * 60) parts.Add("New York");
            return parts.Count == 0 ? "Off-hours" : string.Join("/", parts);
        }

        private static void AppendNext48h(StringBuilder sb, DateTime utcNow)
        {
            // Mark session start events in next 48h
            int[] startsUtc = { 0, 7, 12, 21 }; // Tokyo, London, NY, Sydney
            string[] names = { "Tokyo", "London", "New York", "Sydney" };
            for (int h = 0; h < 48; h++)
            {
                var t = utcNow.Date.AddHours(utcNow.Hour).AddHours(h);
                // align to hour boundary looking forward
                var candidate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, 0, 0, DateTimeKind.Utc).AddHours(h + 1);
                for (int i = 0; i < startsUtc.Length; i++)
                {
                    if (candidate.Hour == startsUtc[i] && candidate <= utcNow.AddHours(48) && candidate > utcNow)
                    {
                        var pt = candidate.AddHours(-8);
                        sb.AppendLine("  " + pt.ToString("ddd HH:mm") + " PT  ·  " + names[i] + " starts");
                    }
                }
            }
        }
    }
}
