using System.IO;
using System.Text.Json;
using Hermes.BinanceDemoFuturesTerminal.Services;
using Hermes.BinanceDemoFuturesTerminal.ViewModels;
using Hermes.Terminals.Shared.Bridge;

namespace Hermes.BinanceDemoFuturesTerminal.Bridge;

public sealed class FuturesBridgeCommandProcessor : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly MainViewModel _viewModel;
    private readonly Timer _timer;
    private readonly object _sync = new();

    public FuturesBridgeCommandProcessor(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        FuturesBridgePaths.EnsureRoot();
        if (!File.Exists(FuturesBridgePaths.CommandsFile))
        {
            File.WriteAllText(
                FuturesBridgePaths.CommandsFile,
                JsonSerializer.Serialize(new FuturesPlatformCommandFile(), JsonOptions));
        }

        _timer = new Timer(_ => ProcessPending(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void ProcessPending()
    {
        List<FuturesPlatformCommand> commands;
        lock (_sync)
        {
            if (!File.Exists(FuturesBridgePaths.CommandsFile))
            {
                return;
            }

            FuturesPlatformCommandFile? file;
            try
            {
                file = JsonSerializer.Deserialize<FuturesPlatformCommandFile>(
                    File.ReadAllText(FuturesBridgePaths.CommandsFile),
                    JsonOptions);
            }
            catch
            {
                return;
            }

            if (file?.Pending is not { Count: > 0 })
            {
                return;
            }

            commands = file.Pending.ToList();
            File.WriteAllText(
                FuturesBridgePaths.CommandsFile,
                JsonSerializer.Serialize(new FuturesPlatformCommandFile(), JsonOptions));
        }

        foreach (var cmd in commands)
        {
            var copy = cmd;
            _ = Task.Run(() => ExecuteSafe(copy));
        }
    }

    private void ExecuteSafe(FuturesPlatformCommand cmd)
    {
        AppServices.Log.Info(
            $"[bridge] execute id={cmd.Id} action={cmd.Action} symbol={cmd.Symbol} side={cmd.Side} "
            + $"qty_usdt={cmd.QuantityUsdt} qty={cmd.Quantity}");
        try
        {
            var result = _viewModel.ExecuteBridgeCommandAsync(cmd).GetAwaiter().GetResult();
            WriteResult(result);
            AppServices.Log.Info($"[bridge] result id={cmd.Id} ok={result.Success} msg={result.Message}");
        }
        catch (Exception ex)
        {
            AppServices.Log.Error($"[bridge] execute failed id={cmd.Id}: {ex.Message}");
            WriteResult(new FuturesPlatformCommandResultFile
            {
                CommandId = cmd.Id,
                Success = false,
                Message = ex.Message,
            });
        }
    }

    private static void WriteResult(FuturesPlatformCommandResultFile result)
    {
        FuturesBridgePaths.EnsureRoot();
        var path = Path.Combine(FuturesBridgePaths.BridgeRoot, $"result-{result.CommandId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
    }

    public void Dispose() => _timer.Dispose();
}
