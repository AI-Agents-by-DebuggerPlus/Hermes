using System.IO;
using System.Windows;
using Hermes.BinanceDemoFuturesTerminal.Services;
using Hermes.BinanceDemoFuturesTerminal.ViewModels;
using Hermes.Terminals.Shared.Bridge;

namespace Hermes.BinanceDemoFuturesTerminal.Bridge;

public sealed class FuturesBridgePublisher : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Timer _heartbeatTimer;
    private readonly object _sync = new();
    private DateTimeOffset _lastPublish = DateTimeOffset.MinValue;

    public FuturesBridgePublisher(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        FuturesBridgePaths.EnsureRoot();
        UnifiedBridgePaths.EnsureTradingBridgeRoot();
        PublishNow();
        _heartbeatTimer = new Timer(_ => WriteHeartbeat(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void RequestPublish()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastPublish < TimeSpan.FromMilliseconds(400))
            {
                return;
            }

            _lastPublish = now;
        }

        PublishNow();
    }

    private void PublishNow()
    {
        try
        {
            FuturesTerminalSnapshotSection section;
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                section = _viewModel.BuildBridgeSnapshot();
            }
            else
            {
                section = Application.Current.Dispatcher.Invoke(_viewModel.BuildBridgeSnapshot);
            }

            var path = UnifiedBridgePaths.UnifiedSnapshotFile;
            var existing = UnifiedSnapshotIO.Read(path);
            var unified = new UnifiedTerminalSnapshotFile
            {
                SchemaVersion = 2,
                TimestampUtc = DateTimeOffset.UtcNow,
                TradingPlatform = existing.TradingPlatform,
                SpotTerminal = existing.SpotTerminal,
                FuturesTerminal = section,
                Agent = existing.Agent,
                Skills = existing.Skills,
            };

            UnifiedSnapshotIO.WriteAtomic(path, unified);
            WriteHeartbeat();
        }
        catch (Exception ex)
        {
            AppServices.Log.Warn($"[bridge] publish failed: {ex.Message}");
        }
    }

    private static void WriteHeartbeat()
    {
        FuturesBridgePaths.EnsureRoot();
        var stamp = DateTimeOffset.UtcNow.ToString("O");
        File.WriteAllText(FuturesBridgePaths.HeartbeatFile, stamp);
        UnifiedBridgePaths.EnsureTradingBridgeRoot();
        File.WriteAllText(UnifiedBridgePaths.UnifiedHeartbeatFile, stamp);
    }

    public void Dispose()
    {
        _heartbeatTimer.Dispose();
        try
        {
            if (File.Exists(FuturesBridgePaths.HeartbeatFile))
            {
                File.Delete(FuturesBridgePaths.HeartbeatFile);
            }
        }
        catch
        {
            // ignore
        }
    }
}
