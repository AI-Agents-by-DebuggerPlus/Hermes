using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Hermes.Wpf.Launcher;

public partial class MainWindow : Window
{
    private readonly string? _scriptPath;
    private readonly string? _repoRoot;
    private CancellationTokenSource? _runCts;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        (_repoRoot, _scriptPath) = ResolvePaths();
        PathLabel.Text = _scriptPath is null
            ? "scripts/run_hermes_wpf.ps1 not found (open from Hermes repo build)."
            : $"Script: {_scriptPath}";
        AppendLog($"Repo: {_repoRoot ?? "(unknown)"}");
    }

    private static (string? repoRoot, string? scriptPath) ResolvePaths()
    {
        foreach (var start in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                     Path.GetDirectoryName(Environment.ProcessPath) ?? ""
                 })
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var script = Path.Combine(dir.FullName, "scripts", "run_hermes_wpf.ps1");
                var csproj = Path.Combine(dir.FullName, "Hermes.Wpf", "Hermes.Wpf.csproj");
                if (File.Exists(script) && File.Exists(csproj))
                {
                    return (dir.FullName, script);
                }

                dir = dir.Parent;
            }
        }

        return (null, null);
    }

    private string SelectedConfiguration() =>
        ConfigCombo.SelectedIndex == 1 ? "Release" : "Debug";

    private string? TradingAnalyticsRoot()
    {
        if (_repoRoot is null)
        {
            return null;
        }

        var sibling = Path.GetFullPath(Path.Combine(_repoRoot, "..", "HermesProjects", "Trading Analytics"));
        return Directory.Exists(sibling) ? sibling : null;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (busy)
        {
            StatusLabel.Text = "Running...";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xD1, 0x2F));
        }

        foreach (var btn in new[]
                 {
                     BtnRebuild, BtnRelaunch, BtnPull, BtnBuildOnly, BtnClose,
                     BtnTestDensityUi, BtnTestDensity, BtnTestDensityFutures, BtnTestBounce,
                     BtnTestFutures, BtnTestSpot, BtnTestAll, BtnTestQa, BtnTestChecklist
                 })
        {
            btn.IsEnabled = !busy;
        }

        ConfigCombo.IsEnabled = !busy;
    }

    private void SetOkStatus(string text = "OK")
    {
        StatusLabel.Text = text;
        StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x0E, 0xC9, 0x70));
    }

    private void SetFailStatus(string text)
    {
        StatusLabel.Text = text;
        StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xF6, 0x46, 0x5D));
    }

    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(line));
            return;
        }

        var stamp = DateTime.Now.ToString("HH:mm:ss");
        LogBox.AppendText($"[{stamp}] {line}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private async void RebuildLaunch_Click(object sender, RoutedEventArgs e) =>
        await RunScriptAsync(gitPull: false, skipBuild: false, noLaunch: false);

    private async void Relaunch_Click(object sender, RoutedEventArgs e) =>
        await RunScriptAsync(gitPull: false, skipBuild: true, noLaunch: false);

    private async void PullRebuild_Click(object sender, RoutedEventArgs e) =>
        await RunScriptAsync(gitPull: true, skipBuild: false, noLaunch: false);

    private async void BuildOnly_Click(object sender, RoutedEventArgs e) =>
        await RunScriptAsync(gitPull: false, skipBuild: false, noLaunch: true);

    private async void CloseOnly_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            AppendLog("Closing Hermes.Wpf processes...");
            await Task.Run(() =>
            {
                foreach (var p in Process.GetProcessesByName("Hermes.Wpf"))
                {
                    try
                    {
                        var id = p.Id;
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(5000);
                        Dispatcher.Invoke(() => AppendLog($"  stop PID={id}"));
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendLog($"  warn: {ex.Message}"));
                    }
                }
            });
            AppendLog("Done.");
            SetOkStatus();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void TestDensity_Click(object sender, RoutedEventArgs e) => LaunchDensityScreener("spot");

    private void TestDensityFutures_Click(object sender, RoutedEventArgs e) =>
        LaunchDensityScreener("futures-demo");

    private void TestDensityUi_Click(object sender, RoutedEventArgs e)
    {
        if (_repoRoot is null)
        {
            AppendLog("ERROR: repo root unknown.");
            return;
        }

        var cfg = SelectedConfiguration();
        var exe = Path.Combine(
            _repoRoot, "Hermes.DensityHeatmap", "bin", cfg, "net8.0-windows", "Hermes.DensityHeatmap.exe");
        if (!File.Exists(exe))
        {
            AppendLog($"Building Density Heatmap ({cfg})...");
            var csproj = Path.Combine(_repoRoot, "Hermes.DensityHeatmap", "Hermes.DensityHeatmap.csproj");
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{csproj}\" -c {cfg} --nologo",
                WorkingDirectory = _repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            p?.WaitForExit(120_000);
            if (!File.Exists(exe))
            {
                AppendLog($"SKIP Heatmap: missing {exe}");
                SetFailStatus("Heatmap missing");
                return;
            }
        }

        Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
        AppendLog($"STARTED Density Heatmap: {exe}");
        SetOkStatus("Heatmap started");
    }

    private void TestBounce_Click(object sender, RoutedEventArgs e)
    {
        if (_repoRoot is null)
        {
            return;
        }

        var root = Path.Combine(_repoRoot, "Source", "ClaudeDensityScreener");
        var script = Path.Combine(root, "scripts", "run_bounce_strategy.ps1");
        if (!File.Exists(script))
        {
            AppendLog($"SKIP Bounce: missing {script}");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            WorkingDirectory = root,
            UseShellExecute = true,
        });
        AppendLog("STARTED Bounce strategy (dry-run)");
        SetOkStatus("Bounce dry-run");
    }

    private void TestFutures_Click(object sender, RoutedEventArgs e) =>
        LaunchExeUnderConfig("Hermes.BinanceDemoFuturesTerminal", "Hermes.BinanceDemoFuturesTerminal.exe", "Futures");

    private void TestSpot_Click(object sender, RoutedEventArgs e) =>
        LaunchExeUnderConfig("Hermes.BinanceDemoSpotTerminal", "Hermes.BinanceDemoSpotTerminal.exe", "Spot");

    private void TestAll_Click(object sender, RoutedEventArgs e)
    {
        TestDensityUi_Click(sender, e);
        LaunchDensityScreener("spot");
        LaunchExeUnderConfig("Hermes.BinanceDemoFuturesTerminal", "Hermes.BinanceDemoFuturesTerminal.exe", "Futures");
        LaunchExeUnderConfig("Hermes.BinanceDemoSpotTerminal", "Hermes.BinanceDemoSpotTerminal.exe", "Spot");
        SetOkStatus("Test apps launched");
    }

    private async void TestQa_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var ta = TradingAnalyticsRoot();
        if (ta is null)
        {
            AppendLog("ERROR: HermesProjects/Trading Analytics not found next to Hermes repo.");
            SetFailStatus("No Trading Analytics");
            return;
        }

        var script = Path.Combine(ta, "qa", "run_all_checks.ps1");
        if (!File.Exists(script))
        {
            AppendLog($"ERROR: missing {script}");
            SetFailStatus("No QA script");
            return;
        }

        SetBusy(true);
        try
        {
            AppendLog($"QA: {script} -SkipLiveDensity");
            var exit = await RunProcessAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -SkipLiveDensity",
                ta,
                CancellationToken.None);
            AppendLog(exit == 0 ? $"QA exit {exit} OK" : $"QA exit {exit} FAILED");
            if (exit == 0)
            {
                SetOkStatus("QA OK");
            }
            else
            {
                SetFailStatus($"QA failed ({exit})");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            SetFailStatus("QA error");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void TestChecklist_Click(object sender, RoutedEventArgs e)
    {
        var ta = TradingAnalyticsRoot();
        var path = ta is null
            ? null
            : Path.Combine(ta, "qa", "MANUAL_CHECKLIST.md");
        if (path is null || !File.Exists(path))
        {
            // Fallback to kit in Hermes repo
            path = _repoRoot is null
                ? null
                : Path.Combine(_repoRoot, "Docs", "TradingAnalytics", "qa-kit", "MANUAL_CHECKLIST.md");
        }

        if (path is null || !File.Exists(path))
        {
            AppendLog("ERROR: MANUAL_CHECKLIST.md not found.");
            SetFailStatus("No checklist");
            return;
        }

        AppendLog($"Open: {path}");
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        SetOkStatus("Checklist opened");
    }

    private void LaunchExeUnderConfig(string projectFolder, string exeName, string label)
    {
        if (_repoRoot is null)
        {
            AppendLog("ERROR: repo root unknown.");
            return;
        }

        var cfg = SelectedConfiguration();
        var exe = Path.Combine(_repoRoot, projectFolder, "bin", cfg, "net8.0-windows", exeName);
        if (!File.Exists(exe))
        {
            AppendLog($"SKIP {label}: missing {exe}");
            SetFailStatus($"{label} missing");
            return;
        }

        var name = Path.GetFileNameWithoutExtension(exe);
        var running = Process.GetProcessesByName(name);
        if (running.Length > 0)
        {
            AppendLog($"ALREADY RUNNING {label} (PID {string.Join(',', running.Select(p => p.Id))})");
            SetOkStatus($"{label} already up");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = true
        });
        AppendLog($"STARTED {label}: {exe}");
        SetOkStatus($"{label} started");
    }

    private void LaunchDensityScreener(string market = "spot")
    {
        if (_repoRoot is null)
        {
            AppendLog("ERROR: repo root unknown.");
            return;
        }

        var densityRoot = Path.Combine(_repoRoot, "Source", "ClaudeDensityScreener");
        var script = Path.Combine(densityRoot, "scripts", "run_density_screener.ps1");
        if (!File.Exists(script))
        {
            AppendLog($"SKIP Density: missing {script}");
            SetFailStatus("Density missing");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Symbol BTCUSDT -Market {market}",
            WorkingDirectory = densityRoot,
            UseShellExecute = true
        });
        AppendLog($"STARTED Density Screener market={market}: {script}");
        SetOkStatus($"Density {market}");
    }

    private async Task RunScriptAsync(bool gitPull, bool skipBuild, bool noLaunch)
    {
        if (_busy)
        {
            return;
        }

        if (_scriptPath is null || !File.Exists(_scriptPath))
        {
            AppendLog("ERROR: run_hermes_wpf.ps1 not found.");
            MessageBox.Show(
                "Не найден scripts/run_hermes_wpf.ps1.\nСоберите launcher из корня репозитория Hermes.",
                "Hermes.Wpf Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        SetBusy(true);

        var args = new StringBuilder();
        args.Append("-NoProfile -ExecutionPolicy Bypass -File ");
        args.Append('"').Append(_scriptPath).Append('"');
        args.Append(" -Configuration ").Append(SelectedConfiguration());
        if (gitPull)
        {
            args.Append(" -GitPull");
        }

        if (skipBuild)
        {
            args.Append(" -SkipBuild");
        }

        if (noLaunch)
        {
            args.Append(" -NoLaunch");
        }

        AppendLog($"powershell {args}");

        try
        {
            var exit = await RunProcessAsync(
                "powershell.exe",
                args.ToString(),
                _repoRoot ?? Path.GetDirectoryName(_scriptPath)!,
                _runCts.Token);

            AppendLog(exit == 0 ? $"Exit {exit} OK" : $"Exit {exit} FAILED");
            if (exit == 0)
            {
                SetOkStatus();
            }
            else
            {
                SetFailStatus($"Failed ({exit})");
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled.");
            StatusLabel.Text = "Cancelled";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            SetFailStatus("Error");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Task<int> RunProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog(e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            tcs.TrySetResult(process.ExitCode);
            process.Dispose();
        };

        ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignored
            }

            tcs.TrySetCanceled(ct);
        });

        if (!process.Start())
        {
            tcs.TrySetException(new InvalidOperationException("Failed to start powershell."));
            return tcs.Task;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return tcs.Task;
    }
}
