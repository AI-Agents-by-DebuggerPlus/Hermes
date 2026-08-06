using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace WpfTestApp
{
    /// <summary>
    /// Agent ↔ HermesWpfTerminal file IPC (not MT5).
    /// Default dir: HermesProjects/Mt5Terminal/hermes/ipc
    /// </summary>
    internal sealed class TerminalAgentIpc : IDisposable
    {
        private readonly HermesWpfTerminal _ui;
        private readonly DispatcherTimer _timer;
        private readonly string _dir;
        private string _lastCommandId = "";
        private DateTime _lastStatusUtc = DateTime.MinValue;

        public TerminalAgentIpc(HermesWpfTerminal ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _dir = ResolveIpcDir();
            Directory.CreateDirectory(_dir);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _timer.Tick += (_, __) => Tick();
            _timer.Start();

            WriteStatus("ipc_started");
            _ui.LogFromIpc("Agent IPC: " + _dir);
        }

        public static string ResolveIpcDir()
        {
            var env = Environment.GetEnvironmentVariable("HERMES_MT5_IPC_DIR");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();

            return @"D:\Programming\AI_Agents\HermesProjects\Mt5Terminal\hermes\ipc";
        }

        public void Dispose()
        {
            try { _timer.Stop(); } catch { /* ignore */ }
        }

        private void Tick()
        {
            try
            {
                ProcessCommandFile();
                if ((DateTime.UtcNow - _lastStatusUtc).TotalMilliseconds >= 1000)
                    WriteStatus("ok");
            }
            catch (Exception ex)
            {
                try { WriteResult(new AgentResult { ok = false, error = "ipc_tick: " + ex.Message }); } catch { /* ignore */ }
            }
        }

        private string CommandPath => Path.Combine(_dir, "command.json");
        private string ResultPath => Path.Combine(_dir, "result.json");
        private string StatusPath => Path.Combine(_dir, "status.json");

        private void ProcessCommandFile()
        {
            if (!File.Exists(CommandPath))
                return;

            string raw;
            try
            {
                raw = File.ReadAllText(CommandPath, Encoding.UTF8);
            }
            catch
            {
                return; // still being written
            }

            if (string.IsNullOrWhiteSpace(raw))
                return;

            AgentCommand cmd;
            try
            {
                cmd = Deserialize<AgentCommand>(raw) ?? new AgentCommand();
            }
            catch (Exception ex)
            {
                TryDeleteCommand();
                WriteResult(new AgentResult { ok = false, error = "bad_json: " + ex.Message, snapshot = BuildSnapshot("bad_json") });
                return;
            }

            if (string.IsNullOrWhiteSpace(cmd.id))
                cmd.id = Guid.NewGuid().ToString("N");

            if (string.Equals(cmd.id, _lastCommandId, StringComparison.Ordinal))
            {
                TryDeleteCommand();
                return;
            }

            _lastCommandId = cmd.id;
            TryDeleteCommand();

            var action = (cmd.action ?? "").Trim().ToLowerInvariant();
            _ui.LogFromIpc("IPC cmd id=" + cmd.id + " action=" + action);

            try
            {
                Execute(cmd, action);
                WriteResult(new AgentResult
                {
                    ok = true,
                    id = cmd.id,
                    action = action,
                    message = "accepted",
                    snapshot = BuildSnapshot("after:" + action)
                });
                WriteStatus("after:" + action);
            }
            catch (Exception ex)
            {
                WriteResult(new AgentResult
                {
                    ok = false,
                    id = cmd.id,
                    action = action,
                    error = ex.Message,
                    snapshot = BuildSnapshot("error")
                });
            }
        }

        private void Execute(AgentCommand cmd, string action)
        {
            switch (action)
            {
                case "snapshot":
                case "status":
                case "get_status":
                    return;

                case "set_lot":
                    if (cmd.lot.HasValue && cmd.lot.Value > 0)
                        _ui.ApplyAgentLot(cmd.lot.Value);
                    return;

                case "set_real_trading":
                    _ui.ApplyAgentRealTrading(cmd.value ?? true);
                    return;

                case "set_auto_trade":
                    _ui.ApplyAgentAutoTrade(cmd.value ?? true);
                    return;

                case "buy_market":
                case "buy":
                    if (cmd.lot.HasValue && cmd.lot.Value > 0)
                        _ui.ApplyAgentLot(cmd.lot.Value);
                    _ui.ClickAgentButton(_ui.BtnQuickBuyPublic);
                    return;

                case "sell_market":
                case "sell":
                    if (cmd.lot.HasValue && cmd.lot.Value > 0)
                        _ui.ApplyAgentLot(cmd.lot.Value);
                    _ui.ClickAgentButton(_ui.BtnQuickSellPublic);
                    return;

                case "close_all":
                    _ui.ClickAgentButton(_ui.BtnCloseAllPublic);
                    return;

                case "close_slot":
                    {
                        var slot = cmd.slot ?? 0;
                        var btn = _ui.GetCloseSlotButton(slot);
                        if (btn == null)
                            throw new InvalidOperationException("slot out of range 0..7: " + slot);
                        _ui.ClickAgentButton(btn);
                        return;
                    }

                default:
                    throw new InvalidOperationException(
                        "unknown action '" + action +
                        "'. Use: snapshot|set_lot|set_real_trading|set_auto_trade|buy_market|sell_market|close_all|close_slot");
            }
        }

        private void TryDeleteCommand()
        {
            try
            {
                if (File.Exists(CommandPath))
                    File.Delete(CommandPath);
            }
            catch { /* ignore */ }
        }

        private void WriteStatus(string note)
        {
            _lastStatusUtc = DateTime.UtcNow;
            var snap = BuildSnapshot(note);
            AtomicWrite(StatusPath, Serialize(snap));
        }

        private void WriteResult(AgentResult result)
        {
            result.utc = DateTime.UtcNow.ToString("o");
            AtomicWrite(ResultPath, Serialize(result));
        }

        private AgentSnapshot BuildSnapshot(string note)
        {
            return _ui.BuildAgentSnapshot(note);
        }

        private static void AtomicWrite(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content ?? "", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        private static string Serialize<T>(T obj)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static T Deserialize<T>(string json) where T : class
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                var ser = new DataContractJsonSerializer(typeof(T));
                return ser.ReadObject(ms) as T;
            }
        }
    }

    [DataContract]
    internal sealed class AgentCommand
    {
        [DataMember(Name = "id")] public string id { get; set; }
        [DataMember(Name = "action")] public string action { get; set; }
        [DataMember(Name = "slot")] public int? slot { get; set; }
        [DataMember(Name = "lot")] public double? lot { get; set; }
        [DataMember(Name = "value")] public bool? value { get; set; }
    }

    [DataContract]
    internal sealed class AgentResult
    {
        [DataMember(Name = "ok")] public bool ok { get; set; }
        [DataMember(Name = "id")] public string id { get; set; }
        [DataMember(Name = "action")] public string action { get; set; }
        [DataMember(Name = "message")] public string message { get; set; }
        [DataMember(Name = "error")] public string error { get; set; }
        [DataMember(Name = "utc")] public string utc { get; set; }
        [DataMember(Name = "snapshot")] public AgentSnapshot snapshot { get; set; }
    }

    [DataContract]
    internal sealed class AgentSnapshot
    {
        [DataMember(Name = "utc")] public string utc { get; set; }
        [DataMember(Name = "note")] public string note { get; set; }
        [DataMember(Name = "build")] public string build { get; set; }
        [DataMember(Name = "ipc_dir")] public string ipc_dir { get; set; }
        [DataMember(Name = "symbol")] public string symbol { get; set; }
        [DataMember(Name = "bid")] public string bid { get; set; }
        [DataMember(Name = "ask")] public string ask { get; set; }
        [DataMember(Name = "lot")] public string lot { get; set; }
        [DataMember(Name = "account")] public string account { get; set; }
        [DataMember(Name = "market_status")] public string market_status { get; set; }
        [DataMember(Name = "real_trading")] public bool real_trading { get; set; }
        [DataMember(Name = "auto_trade")] public bool auto_trade { get; set; }
        [DataMember(Name = "positions_header")] public string positions_header { get; set; }
        [DataMember(Name = "positions")] public List<string> positions { get; set; }
        [DataMember(Name = "log_tail")] public List<string> log_tail { get; set; }
    }
}
