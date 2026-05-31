namespace Hermes.Wpf.Services;

/// <summary>Outbound safety rules for trading-mode agent (text-only, stricter-wins over terminal risk manager).</summary>
public static class TradingSafetyRulesInstructions
{
    public const string DefaultRulesRu =
        "Маржа на сделку — не более 1% депозита (даже если в риск-менеджере терминала больше).\n"
        + "Максимальный убыток за день — 50 USDT. После лимита — только close_position, без новых входов.\n"
        + "Только BTCUSDT и ETHUSDT для открытия новых позиций.";

    public static string BuildOutboundBlockRu(string? userRules)
    {
        var rules = (userRules ?? string.Empty).Trim();
        if (rules.Length == 0)
        {
            return string.Empty;
        }

        return
            "### Дополнительная защита агента (может быть строже риск-менеджера терминала)\n"
            + "Ты **не исполняешь слепо**. Риск-менеджер терминала — базовая защита на бирже; "
            + "**правила пользователя ниже могут быть строже**.\n\n"
            + "**Правила пользователя:**\n"
            + rules
            + "\n\n"
            + "**Перед JSON skill:trading (place_order / set_leverage):**\n"
            + "1. Сверь параметры ордера с правилами выше **и** с лимитами из snapshot "
            + "(MaxOrderMarginPercent, MaxOrderNotionalUsdt, WalletBalanceUsdt, DailyRealizedPnlUsdt).\n"
            + "2. Для каждого лимита бери **минимум** (самое строгое). Пример: текст «1% маржи», "
            + "а в терминале 5% → действуй как **1%**, предупреди о расхождении настроек.\n"
            + "3. Если правило нарушено — **не отправляй JSON**, объясни на русском, что не так.\n"
            + "4. `close_position` / `close_all_positions` / `cancel_order` — **не блокируй** "
            + "из-за дневного убытка или whitelist (выход всегда разрешён).\n"
            + "5. Если настройки терминала выглядят **неадекватно** (слишком большой риск) — "
            + "предупреди пользователя даже на статусных вопросах.\n";
    }
}
