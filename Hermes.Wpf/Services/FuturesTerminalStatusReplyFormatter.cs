using System.Text;
using Hermes.Terminals.Shared.Bridge;

namespace Hermes.Wpf.Services;

public static class FuturesTerminalStatusReplyFormatter
{
    public static string TerminalUnavailableMessage() =>
        "Binance Demo Futures Terminal не запущен. Нажмите **Binance Futures** в Hermes.Wpf "
        + "или включите FuturesTerminalAutoLaunch в настройках.";

    public static string FormatBalanceOnly(FuturesTerminalSnapshotSection futures)
    {
        if (futures.Balances.Count == 0)
        {
            return futures.HasCredentials
                ? "Futures Demo: балансы пусты или ещё не загружены."
                : "Futures Demo: API-ключи не настроены в терминале.";
        }

        var lines = futures.Balances
            .Take(8)
            .Select(b => $"{b.Asset}: free={b.Free:N4} locked={b.Locked:N4}");
        return "Futures балансы:\n" + string.Join("\n", lines);
    }

    public static string FormatAccountSummary(FuturesTerminalSnapshotSection futures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**Сводка Binance Demo Futures**");
        sb.AppendLine(
            $"Symbol: {futures.SelectedSymbol} | WS: {futures.WsStatus} | "
            + $"Price: {futures.LastPrice:N2} ({futures.ChangePercent24h:+#0.00;-#0.00}%)");
        sb.AppendLine();

        sb.AppendLine("**Балансы**");
        if (futures.Balances.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var b in futures.Balances.Take(12))
            {
                sb.AppendLine($"• {b.Asset}: free={b.Free:N4} locked={b.Locked:N4}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("**Позиции**");
        if (futures.Positions.Count == 0)
        {
            sb.AppendLine("• (нет открытых)");
        }
        else
        {
            foreach (var p in futures.Positions)
            {
                sb.AppendLine(
                    $"• {p.Symbol} {p.Side} size={p.Size} uPnL={p.UnrealizedPnl:N2} {p.Leverage}x {p.MarginType}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("**Открытые ордера**");
        if (futures.OpenOrders.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var o in futures.OpenOrders.Take(10))
            {
                sb.AppendLine($"• #{o.Id} {o.Symbol} {o.Side} {o.Type} qty={o.Quantity} {o.Status}");
            }
        }

        return sb.ToString().Trim();
    }
}
