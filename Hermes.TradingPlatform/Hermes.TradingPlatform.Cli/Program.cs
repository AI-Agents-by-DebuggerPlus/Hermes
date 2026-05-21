using System.Text;
using System.Text.Json;
using Hermes.TradingPlatform.Shared.Bridge;

var argsList = args.ToList();
if (argsList.Count == 0 || argsList[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

TradingBridgePaths.EnsureRoot();
var cmd = argsList[0].ToLowerInvariant();

return cmd switch
{
    "status" => CmdStatus(argsList),
    "enqueue" => CmdEnqueue(string.Join(' ', argsList.Skip(1))),
    "wait-result" => CmdWaitResult(argsList),
    "is-running" => CmdIsRunning(),
    _ => Unknown(cmd),
};

static int CmdStatus(List<string> args)
{
    if (!File.Exists(TradingBridgePaths.SnapshotFile))
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Trading Platform не запущен или bridge ещё не опубликовал snapshot.");
        Console.WriteLine($"Ожидаемый файл: {TradingBridgePaths.SnapshotFile}");
        return 2;
    }

    var json = File.ReadAllText(TradingBridgePaths.SnapshotFile);
    if (args.Any(a => a is "--json" or "-j"))
    {
        Console.WriteLine(json);
        return 0;
    }

    var snap = JsonSerializer.Deserialize<TradingPlatformSnapshotFile>(json);
    if (snap is null)
    {
        Console.WriteLine("Не удалось разобрать snapshot.");
        return 3;
    }

    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine($"=== Hermes Trading Platform @ {snap.TimestampUtc:u} ===");
    Console.WriteLine($"Feed: {snap.FeedStatus} ({snap.MarketDataSource})");
    Console.WriteLine($"Balance {snap.Account.Balance:N2} | Equity {snap.Account.Equity:N2} | Leverage {snap.Account.Leverage:F1}x");
    Console.WriteLine($"PnL today {snap.Pnl.Today:N2} | Risk {snap.Risk.RiskLevel} DD {snap.Risk.DailyDrawdownPercent:F1}%");
    Console.WriteLine($"Hermes orchestrator: {snap.Hermes.State} | {snap.Hermes.ActiveStrategy}");
    Console.WriteLine($"Reasoning: {snap.Hermes.CurrentReasoning}");
    Console.WriteLine("-- Positions --");
    foreach (var p in snap.Positions)
    {
        Console.WriteLine($"  {p.Symbol} {p.Side} size={p.Size} uPnL={p.UnrealizedPnl:N2}");
    }

    Console.WriteLine("-- Open orders --");
    foreach (var o in snap.Orders.Where(o => o.Status == "Open"))
    {
        Console.WriteLine($"  {o.Id} {o.Symbol} {o.Side} {o.Type} {o.Quantity} @ {o.Price:N2} RO={o.ReduceOnly}");
    }

    Console.WriteLine("-- Strategies --");
    foreach (var s in snap.Strategies)
    {
        Console.WriteLine($"  {s.Id} {s.Name} enabled={s.IsEnabled} status={s.Status}");
    }

    return 0;
}

static int CmdEnqueue(string jsonLine)
{
    if (string.IsNullOrWhiteSpace(jsonLine))
    {
        Console.Error.WriteLine("enqueue: передайте JSON команды одной строкой.");
        return 1;
    }

    TradingPlatformCommand? command;
    try
    {
        command = JsonSerializer.Deserialize<TradingPlatformCommand>(jsonLine);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"JSON error: {ex.Message}");
        return 1;
    }

    if (command is null || string.IsNullOrWhiteSpace(command.Action))
    {
        Console.Error.WriteLine("Invalid command JSON");
        return 1;
    }

    var file = File.Exists(TradingBridgePaths.CommandsFile)
        ? JsonSerializer.Deserialize<TradingPlatformCommandFile>(File.ReadAllText(TradingBridgePaths.CommandsFile))
          ?? new TradingPlatformCommandFile()
        : new TradingPlatformCommandFile();

    var list = file.Pending.ToList();
    command.RequestedBy = "Hermes.Wpf.Cli";
    list.Add(command);
    File.WriteAllText(TradingBridgePaths.CommandsFile, JsonSerializer.Serialize(new TradingPlatformCommandFile { Pending = list }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(command.Id);
    return 0;
}

static int CmdWaitResult(List<string> args)
{
    if (args.Count < 2 || !Guid.TryParse(args[1], out var id))
    {
        Console.Error.WriteLine("wait-result <command-guid> [--timeout=15]");
        return 1;
    }

    var timeoutSec = 15;
    foreach (var a in args.Skip(2))
    {
        if (a.StartsWith("--timeout=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a["--timeout=".Length..], out var t))
        {
            timeoutSec = t;
        }
    }

    var path = Path.Combine(TradingBridgePaths.RootDirectory, $"result-{id}.json");
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path))
        {
            Console.WriteLine(File.ReadAllText(path));
            return 0;
        }

        Thread.Sleep(300);
    }

    Console.Error.WriteLine("Timeout waiting for command result. Is Trading Platform running?");
    return 2;
}

static int CmdIsRunning()
{
    if (!File.Exists(TradingBridgePaths.HeartbeatFile))
    {
        Console.WriteLine("false");
        return 2;
    }

    var text = File.ReadAllText(TradingBridgePaths.HeartbeatFile).Trim();
    if (!DateTimeOffset.TryParse(text, out var beat))
    {
        Console.WriteLine("false");
        return 2;
    }

    var alive = DateTimeOffset.UtcNow - beat < TimeSpan.FromSeconds(10);
    Console.WriteLine(alive ? "true" : "false");
    return alive ? 0 : 2;
}

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    PrintHelp();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
Hermes.TradingPlatform.Cli — bridge to running terminal (file IPC)

  status [--json]     Human summary or raw snapshot JSON
  is-running          true if heartbeat fresh (<10s)
  enqueue <json>      Queue command; prints command id (guid)
  wait-result <id>    Poll result file [--timeout=15]

Commands JSON action: place_order | cancel_order | enable_strategy | emergency_stop
""");
}
