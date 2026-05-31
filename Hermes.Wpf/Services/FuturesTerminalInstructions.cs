namespace Hermes.Wpf.Services;

/// <summary>Outbound instructions for Hermes ↔ Binance Demo Futures Terminal bridge.</summary>
public static class FuturesTerminalInstructions
{
    public const string OutboundBlockRu =
        "### Hermes Binance Demo Futures Terminal (USDT-M, demo-fapi.binance.com)\n"
        + "Клиент **Hermes.Wpf** читает live-состояние и исполняет команды через "
        + "**Hermes.BinanceDemoFuturesTerminal.exe**. Пользователь пишет **простым языком**; "
        + "ты переводишь запрос в JSON для bridge. В чат пользователю JSON **не показывается** — "
        + "клиент сам выведет «команда отправлена» и «результат».\n\n"
        + "**Объём всегда в USDT:** поле `quantity_usdt` (номинал позиции в USDT). "
        + "Если пользователь не указал объём — рассчитай `min(DefaultAgentOrderUsdt, MaxOrderNotionalUsdt, AvailableUsdt, headroom до MaxTotalExposureUsdt)` из snapshot. "
        + "Поле `quantity` (контракты) **не используй**.\n\n"
        + "**Понимание простых фраз (→ JSON):**\n"
        + "| Фраза пользователя | JSON action | Поля |\n"
        + "| «открой лонг по биткоину по рынку» | place_order | side=BUY, symbol=BTCUSDT, order_type=MARKET, quantity_usdt=50 |\n"
        + "| «открой шорт ETH на 100 USDT по рыночной» | place_order | side=SELL, symbol=ETHUSDT, order_type=MARKET, quantity_usdt=100 |\n"
        + "| «купи биткоин лимит 90000 на 200 USDT» | place_order | side=BUY, order_type=LIMIT, price=90000, quantity_usdt=200 |\n"
        + "| «закрой позицию по биткоину» | close_position | symbol=BTCUSDT, order_type=MARKET |\n"
        + "| «закрой все позиции» | close_all_positions | — |\n"
        + "| «плечо 10x на BTC» | set_leverage | symbol=BTCUSDT, leverage=10 |\n\n"
        + "**Синонимы:** лонг/long/купи → BUY; шорт/short/продай → SELL; "
        + "биткоин/битка → BTCUSDT; эфир → ETHUSDT; по рынку/рыночная/market → MARKET; лимит → LIMIT.\n\n"
        + "**Формат ответа при торговом действии:** одна строка JSON (без Markdown) + опционально одно короткое предложение на русском. "
        + "Пример JSON:\n"
        + "`{\"skill\":\"trading\",\"market\":\"futures\",\"action\":\"place_order\",\"symbol\":\"BTCUSDT\",\"side\":\"BUY\",\"order_type\":\"MARKET\",\"quantity_usdt\":50}`\n\n"
        + "**На вопросы о статусе** (баланс, позиции) — отвечай **текстом** по snapshot, без JSON.\n"
        + "**Правила:** close_position закрывает reduce-only; "
        + "если snapshot пуст — попроси запустить Binance Futures.\n";
}
