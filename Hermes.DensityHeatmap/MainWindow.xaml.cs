using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hermes.DensityHeatmap;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LadderRow> _rows = [];
    private readonly DispatcherTimer _timer;
    private FileSystemWatcher? _watcher;
    private readonly string _snapshotPath = DensitySnapshotIO.DefaultSnapshotPath();

    public MainWindow()
    {
        InitializeComponent();
        LadderList.ItemsSource = _rows;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Reload();
        _timer.Start();
        TryWatch();
        Reload();
    }

    private void TryWatch()
    {
        try
        {
            var dir = Path.GetDirectoryName(_snapshotPath);
            if (dir is null)
            {
                return;
            }

            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_snapshotPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += (_, _) => Dispatcher.BeginInvoke(Reload);
            _watcher.Created += (_, _) => Dispatcher.BeginInvoke(Reload);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Watcher: " + ex.Message;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void StartSpot_Click(object sender, RoutedEventArgs e) => StartScreener("spot");

    private void StartFutures_Click(object sender, RoutedEventArgs e) => StartScreener("futures-demo");

    private void StartScreener(string market)
    {
        var repo = FindHermesRepo();
        if (repo is null)
        {
            MessageBox.Show("Hermes repo not found (need Source/ClaudeDensityScreener).", "Density Heatmap");
            return;
        }

        var root = Path.Combine(repo, "Source", "ClaudeDensityScreener");
        var script = Path.Combine(root, "scripts", "run_density_screener.ps1");
        if (!File.Exists(script))
        {
            MessageBox.Show("Missing " + script, "Density Heatmap");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Symbol BTCUSDT -Market {market}",
            WorkingDirectory = root,
            UseShellExecute = true,
        });
        StatusLabel.Text = $"Started screener market={market}";
    }

    private static string? FindHermesRepo()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Source", "ClaudeDensityScreener")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }

    private void Reload()
    {
        var snap = DensitySnapshotIO.TryRead(_snapshotPath);
        var age = DensitySnapshotIO.HeartbeatAgeSeconds();
        if (snap is null)
        {
            MetaLabel.Text = "No snapshot yet — start screener";
            StatusLabel.Text = $"Waiting for {_snapshotPath}";
            _rows.Clear();
            AlertList.Items.Clear();
            return;
        }

        var stale = age is null or > 15;
        MetaLabel.Text =
            $"{snap.Symbol}  market={snap.Market}  mid={snap.CurrentPrice:F2}  levels={snap.Levels.Count}"
            + (stale ? "  [STALE heartbeat]" : "  [OK]");

        var mid = snap.CurrentPrice ?? 0;
        var maxVol = snap.Levels.Count == 0 ? 1 : Math.Max(1e-9, snap.Levels.Max(l => l.Volume));

        var ordered = snap.Levels
            .OrderByDescending(l => l.Price)
            .Take(40)
            .ToList();

        _rows.Clear();
        foreach (var lvl in ordered)
        {
            var isBid = string.Equals(lvl.Side, "bid", StringComparison.OrdinalIgnoreCase);
            var intensity = Math.Clamp(lvl.Strength, 0, 1);
            var color = isBid
                ? Color.FromRgb((byte)(20), (byte)(40 + 140 * intensity), (byte)(40))
                : Color.FromRgb((byte)(40 + 160 * intensity), (byte)(30), (byte)(30));
            if (Math.Abs(lvl.DistancePct ?? 99) < 0.05)
            {
                color = Color.FromRgb(0xF8, 0xD1, 0x2F);
            }

            _rows.Add(new LadderRow
            {
                PriceText = lvl.Price.ToString("F2"),
                MetaText = $"{lvl.Side} {lvl.Strength:F2} {lvl.Source}",
                BarWidth = 20 + 180 * (lvl.Volume / maxVol),
                BarBrush = new SolidColorBrush(color),
            });
        }

        AlertList.Items.Clear();
        foreach (var lvl in snap.Levels
                     .Where(l => Math.Abs(l.DistancePct ?? 99) <= 0.12)
                     .OrderBy(l => Math.Abs(l.DistancePct ?? 99))
                     .Take(8))
        {
            AlertList.Items.Add(
                $"{lvl.Side.ToUpperInvariant()} @ {lvl.Price:F2}  dist={lvl.DistancePct:F3}%  str={lvl.Strength:F2}  {lvl.Source}");
        }

        if (AlertList.Items.Count == 0)
        {
            AlertList.Items.Add(mid > 0 ? "No walls within 0.12% of mid" : "No mid price");
        }

        StatusLabel.Text =
            $"File: {_snapshotPath}  heartbeat age={(age is null ? "n/a" : $"{age:F1}s")}";
    }

    private sealed class LadderRow
    {
        public string PriceText { get; set; } = "";
        public string MetaText { get; set; } = "";
        public double BarWidth { get; set; }
        public Brush BarBrush { get; set; } = Brushes.Gray;
    }
}
