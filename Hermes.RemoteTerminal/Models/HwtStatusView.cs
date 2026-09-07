using System.Text.Json;

namespace Hermes.RemoteTerminal.Models;

public sealed class HwtStatusView
{
    public string Source { get; set; } = string.Empty;
    public string Utc { get; set; } = string.Empty;
    public string Build { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Bid { get; set; } = string.Empty;
    public string Ask { get; set; } = string.Empty;
    public string Lot { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string MarketStatus { get; set; } = string.Empty;
    public bool RealTrading { get; set; }
    public bool AutoTrade { get; set; }
    public string PositionsHeader { get; set; } = string.Empty;
    public List<string> Positions { get; set; } = [];
    public List<string> PendingOrders { get; set; } = [];

    public IReadOnlyList<string> OpenPositions
    {
        get
        {
            var open = new List<string>();
            foreach (var p in Positions)
            {
                if (LooksPending(p))
                {
                    continue;
                }

                open.Add(p);
            }

            return open;
        }
    }

    public IReadOnlyList<string> ResolvedPending
    {
        get
        {
            if (PendingOrders.Count > 0)
            {
                return PendingOrders;
            }

            var pending = new List<string>();
            foreach (var p in Positions)
            {
                if (LooksPending(p))
                {
                    pending.Add(p);
                }
            }

            return pending;
        }
    }

    private static bool LooksPending(string line)
    {
        var s = line ?? string.Empty;
        return s.Contains("pending", StringComparison.OrdinalIgnoreCase)
               || s.Contains("buy limit", StringComparison.OrdinalIgnoreCase)
               || s.Contains("sell limit", StringComparison.OrdinalIgnoreCase)
               || s.Contains("buy stop", StringComparison.OrdinalIgnoreCase)
               || s.Contains("sell stop", StringComparison.OrdinalIgnoreCase)
               || s.Contains("отлож", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string? content, out HwtStatusView view)
    {
        view = new HwtStatusView();
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var t = content.Trim();
        if (!t.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(t);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            {
                var type = typeEl.GetString();
                if (!string.Equals(type, "hwt_status", StringComparison.OrdinalIgnoreCase)
                    && !LooksLikeRawStatus(root))
                {
                    return false;
                }
            }
            else if (!LooksLikeRawStatus(root))
            {
                return false;
            }

            view.Utc = GetStr(root, "utc");
            view.Build = GetStr(root, "build");
            view.Symbol = GetStr(root, "symbol");
            view.Bid = GetStr(root, "bid");
            view.Ask = GetStr(root, "ask");
            view.Lot = GetStr(root, "lot");
            view.Account = GetStr(root, "account");
            view.MarketStatus = GetStr(root, "market_status");
            view.PositionsHeader = GetStr(root, "positions_header");
            view.RealTrading = GetBool(root, "real_trading");
            view.AutoTrade = GetBool(root, "auto_trade");
            ReadStringArray(root, "positions", view.Positions);
            ReadStringArray(root, "pending_orders", view.PendingOrders);
            ReadStringArray(root, "pending", view.PendingOrders);

            return LooksLikeRawStatus(root) || view.Positions.Count > 0 || view.PendingOrders.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeRawStatus(JsonElement root) =>
        root.TryGetProperty("symbol", out _)
        || root.TryGetProperty("bid", out _)
        || root.TryGetProperty("real_trading", out _)
        || root.TryGetProperty("account", out _);

    public string FormatPanel()
    {
        var lines = new List<string>
        {
            $"HWT [{Source}]  {Symbol}  Bid {Bid}  Ask {Ask}  Lot {Lot}",
            $"Real={RealTrading}  Auto={AutoTrade}  Acc={Account}",
            string.IsNullOrWhiteSpace(MarketStatus) ? "(no market status)" : MarketStatus,
        };
        if (!string.IsNullOrWhiteSpace(PositionsHeader))
        {
            lines.Add(PositionsHeader);
        }

        if (Positions.Count == 0)
        {
            lines.Add("positions: (none)");
        }
        else
        {
            foreach (var p in Positions)
            {
                lines.Add("  " + p);
            }
        }

        if (!string.IsNullOrWhiteSpace(Build))
        {
            lines.Add("build: " + Build);
        }

        if (!string.IsNullOrWhiteSpace(Utc))
        {
            lines.Add("utc: " + Utc);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void ReadStringArray(JsonElement root, string name, List<string> target)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var s = p.GetString();
            if (!string.IsNullOrWhiteSpace(s) && !target.Contains(s!))
            {
                target.Add(s!);
            }
        }
    }

    private static string GetStr(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;
}
