namespace Hermes.Wpf.Services;

/// <summary>Outbound instructions for Hermes ↔ Hermes.SpotTerminal bridge.</summary>
public static class SpotTerminalInstructions
{
    public const string OutboundBlockRu =
        "### Hermes Spot Terminal (spot / Binance Demo)\n"
        + "Клиент **Hermes.Wpf** читает live-состояние и ставит команды в очередь через **Hermes.SpotTerminal.exe** "
        + "(bridge: `%LocalAppData%\\HermesTrading\\bridge\\`, команды: `%LocalAppData%\\HermesSpot\\bridge\\`).\n\n"
        + "**Ты можешь:**\n"
        + "- Отвечать на вопросы: балансы spot, открытые ордера, тикеры, agent/skills — по блоку «Spot Terminal snapshot» ниже.\n\n"
        + "**Торговые действия из чата** — только JSON (без Markdown), один объект на ответ:\n"
        + "• Spot ордер: `{\"skill\":\"trading\",\"action\":\"place_order\",\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"order_type\":\"Market\",\"quantity\":0.001,\"price\":0}`\n"
        + "• Отмена: `{\"skill\":\"trading\",\"action\":\"cancel_order\",\"symbol\":\"BTCUSDT\",\"order_id\":\"123\"}`\n"
        + "• Режим: `{\"skill\":\"trading\",\"action\":\"set_mode\",\"order_type\":\"SpotDemo\"}` (Virtual | SpotDemo)\n\n"
        + "**Правила:**\n"
        + "- Исполнение через **Hermes.SpotTerminal**, не Hermes.TradingPlatform.\n"
        + "- Если snapshot пуст — попроси запустить SpotTerminal (кнопка SpotTerminal в Hermes.Wpf).\n"
        + "- SpotDemo = Binance Spot Demo (demo-api.binance.com, ключи из Demo Trading); Virtual = симуляция без ключей.\n";
}
