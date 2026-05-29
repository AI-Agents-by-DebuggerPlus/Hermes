using System.Text;
using Hermes.SpotTerminal.Shared.Bridge;

namespace Hermes.Wpf.Services;

public static class SpotTerminalStatusReplyFormatter
{
    public static string FormatBalanceOnly(SpotTerminalSnapshotSection spot)
    {
        if (spot.Balances.Count == 0)
        {
            return $"Spot ({spot.ExecutionMode}): балансы пусты или ещё не загружены.";
        }

        var lines = spot.Balances
            .Take(8)
            .Select(b => $"{b.Asset}: free={b.Free:N4} locked={b.Locked:N4}");
        return "Spot балансы:\n" + string.Join("\n", lines);
    }

    public static string FormatAccountSummary(SpotTerminalSnapshotSection spot, AgentSnapshotSection? agent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**Сводка Spot Terminal**");
        sb.AppendLine($"Режим: {spot.ExecutionMode} | Feed: {spot.FeedStatus}");
        sb.AppendLine();

        sb.AppendLine("**Балансы**");
        if (spot.Balances.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var b in spot.Balances.Take(12))
            {
                sb.AppendLine($"• {b.Asset}: free={b.Free:N4} locked={b.Locked:N4}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("**Тикеры**");
        if (spot.Tickers.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var t in spot.Tickers.Take(8))
            {
                sb.AppendLine($"• {t.Symbol}: {t.Price:N4} ({t.ChangePercent24h:F2}%)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("**Открытые ордера**");
        if (spot.OpenOrders.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var o in spot.OpenOrders.Take(10))
            {
                sb.AppendLine($"• {o.Id} {o.Symbol} {o.Side} {o.Type} qty={o.Quantity} @ {o.Price:N4} [{o.Status}]");
            }
        }

        if (agent is not null)
        {
            sb.AppendLine();
            sb.AppendLine("**Agent**");
            sb.AppendLine($"• {agent.SessionState} | {agent.CurrentThought}");
        }

        return sb.ToString().Trim();
    }

    public static string TerminalUnavailableMessage() =>
        "Нет данных Spot Terminal: запустите Hermes.SpotTerminal.exe (кнопка SpotTerminal или автозапуск в Settings).";
}
