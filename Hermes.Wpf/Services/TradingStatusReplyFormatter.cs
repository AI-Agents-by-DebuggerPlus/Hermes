using System.Text;
using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

public static class TradingStatusReplyFormatter
{
    public static string FormatBalanceOnly(TradingPlatformSnapshotFile snap)
    {
        return $"Баланс (paper): {snap.Account.Balance:N2}";
    }

    public static string FormatAccountSummary(TradingPlatformSnapshotFile snap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**Сводка по счёту** (paper, Hermes Trading Platform)");
        sb.AppendLine($"Время: {snap.TimestampUtc:u} | {snap.FeedStatus} | {snap.MarketDataSource}");
        sb.AppendLine();
        sb.AppendLine("**Счёт**");
        sb.AppendLine($"• Баланс: {snap.Account.Balance:N2}");
        sb.AppendLine($"• Equity: {snap.Account.Equity:N2}");
        sb.AppendLine($"• Свободная маржа: {snap.Account.FreeMargin:N2}");
        sb.AppendLine($"• Использованная маржа: {snap.Account.UsedMargin:N2}");
        sb.AppendLine($"• Плечо: {snap.Account.Leverage:F1}x");
        sb.AppendLine();
        sb.AppendLine("**PnL**");
        sb.AppendLine($"• Сегодня: {snap.Pnl.Today:N2} | Неделя: {snap.Pnl.Week:N2} | Месяц: {snap.Pnl.Month:N2} | Всего: {snap.Pnl.AllTime:N2}");
        sb.AppendLine();
        sb.AppendLine("**Риск**");
        sb.AppendLine(
            $"• Уровень: {snap.Risk.RiskLevel} | DD: {snap.Risk.DailyDrawdownPercent:F1}% | Exposure: {snap.Risk.ExposurePercent:F1}%");
        sb.AppendLine($"• Safe Mode: {(snap.Risk.SafeMode ? "да" : "нет")} | Emergency halt: {(snap.Risk.EmergencyHalt ? "да" : "нет")}");
        sb.AppendLine();
        sb.AppendLine("**Hermes orchestrator**");
        sb.AppendLine($"• {snap.Hermes.State} | {snap.Hermes.ActiveStrategy} | confidence {snap.Hermes.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(snap.Hermes.CurrentReasoning))
        {
            sb.AppendLine($"• {snap.Hermes.CurrentReasoning}");
        }

        sb.AppendLine();
        sb.AppendLine("**Позиции**");
        if (snap.Positions.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var p in snap.Positions)
            {
                sb.AppendLine(
                    $"• {p.Symbol} {p.Side} size={p.Size} entry={p.EntryPrice:N2} mark={p.MarkPrice:N2} uPnL={p.UnrealizedPnl:N2}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("**Открытые ордера**");
        var open = snap.Orders.Where(o => o.Status == "Open").ToList();
        if (open.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var o in open)
            {
                sb.AppendLine(
                    $"• {o.Id} {o.Symbol} {o.Side} {o.Type} qty={o.Quantity} @ {o.Price:N2} RO={o.ReduceOnly}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(
            "**Стратегии:** "
            + string.Join(", ", snap.Strategies.Select(s => $"{s.Name} ({s.Id}) {(s.IsEnabled ? "on" : "off")}")));

        return sb.ToString().Trim();
    }

    public static string TerminalUnavailableMessage() =>
        "Нет данных Trading Platform: запустите Hermes.TradingPlatform.exe (bridge без свежего snapshot).";
}
