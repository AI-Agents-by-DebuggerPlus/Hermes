using System.Text;
using System.Text.Json;
using Hermes.SpotTerminal.Shared.Bridge;
using Hermes.Terminals.Shared.Bridge;

var argsList = args.ToList();
if (argsList.Count == 0 || argsList[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

UnifiedBridgePaths.EnsureTradingBridgeRoot();
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
    var path = UnifiedBridgePaths.UnifiedSnapshotFile;
    if (!File.Exists(path))
    {
        Console.WriteLine("Spot/Unified snapshot not found. Start Hermes.SpotTerminal or TradingPlatform.");
        return 2;
    }

    var json = File.ReadAllText(path);
    if (args.Any(a => a is "--json" or "-j"))
    {
        Console.WriteLine(json);
        return 0;
    }

    var snap = UnifiedSnapshotIO.Read(path);
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine($"=== Unified snapshot @ {snap.TimestampUtc:u} ===");
    if (snap.SpotTerminal is not null)
    {
        Console.WriteLine($"Spot: {snap.SpotTerminal.ExecutionMode} | {snap.SpotTerminal.FeedStatus}");
        foreach (var b in snap.SpotTerminal.Balances.Take(5))
        {
            Console.WriteLine($"  {b.Asset}: free={b.Free} locked={b.Locked}");
        }
    }

    if (snap.Agent is not null)
    {
        Console.WriteLine($"Agent: {snap.Agent.SessionState} | {snap.Agent.CurrentThought}");
    }

    if (snap.Skills is not null)
    {
        Console.WriteLine($"Skills: draft={snap.Skills.DraftCount} approved={snap.Skills.ApprovedCount}");
    }

    return 0;
}

static int CmdEnqueue(string jsonLine)
{
    if (string.IsNullOrWhiteSpace(jsonLine))
    {
        Console.Error.WriteLine("enqueue: pass JSON command");
        return 1;
    }

    var command = JsonSerializer.Deserialize<SpotPlatformCommand>(jsonLine, CliJson.Options);
    if (command is null || string.IsNullOrWhiteSpace(command.Action))
    {
        Console.Error.WriteLine("enqueue: invalid JSON or missing action");
        return 1;
    }

    SpotBridgePaths.EnsureRoot();
    var file = File.Exists(SpotBridgePaths.CommandsFile)
        ? JsonSerializer.Deserialize<SpotPlatformCommandFile>(File.ReadAllText(SpotBridgePaths.CommandsFile))
          ?? new SpotPlatformCommandFile()
        : new SpotPlatformCommandFile();

    var list = file.Pending.ToList();
    command.RequestedBy = "Hermes.SpotTerminal.Cli";
    list.Add(command);
    File.WriteAllText(SpotBridgePaths.CommandsFile, JsonSerializer.Serialize(new SpotPlatformCommandFile { Pending = list }, CliJson.Options));
    Console.WriteLine(command.Id);
    return 0;
}

static int CmdWaitResult(List<string> args)
{
    if (args.Count < 2 || !Guid.TryParse(args[1], out var id))
    {
        return 1;
    }

    var path = Path.Combine(SpotBridgePaths.BridgeRoot, $"result-{id}.json");
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path))
        {
            Console.WriteLine(File.ReadAllText(path));
            return 0;
        }

        Thread.Sleep(300);
    }

    Console.Error.WriteLine($"wait-result: timeout ({id}) — is Hermes.SpotTerminal running?");
    return 2;
}

static int CmdIsRunning()
{
    if (!File.Exists(UnifiedBridgePaths.UnifiedHeartbeatFile))
    {
        Console.WriteLine("false");
        return 2;
    }

    var text = File.ReadAllText(UnifiedBridgePaths.UnifiedHeartbeatFile).Trim();
    if (!DateTimeOffset.TryParse(text, out var beat))
    {
        Console.WriteLine("false");
        return 2;
    }

    var alive = DateTimeOffset.UtcNow - beat < TimeSpan.FromSeconds(12);
    Console.WriteLine(alive ? "true" : "false");
    return alive ? 0 : 2;
}

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"Unknown: {cmd}");
    PrintHelp();
    return 1;
}

static void PrintHelp() =>
    Console.WriteLine("""
Hermes.SpotTerminal.Cli
  status [--json]
  is-running
  enqueue <json>
  wait-result <guid>
""");

file static class CliJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
