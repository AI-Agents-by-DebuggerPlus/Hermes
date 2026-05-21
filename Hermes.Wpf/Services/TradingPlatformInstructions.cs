namespace Hermes.Wpf.Services;

/// <summary>Outbound instructions for Hermes ↔ Hermes Trading Platform bridge (Phase 6 integration).</summary>
public static class TradingPlatformInstructions
{
    public const string OutboundBlockRu =
        "### Hermes Trading Platform (paper terminal, отдельное приложение)\n"
        + "Клиент **Hermes.Wpf** может читать live-состояние терминала и ставить команды в очередь, если запущен **Hermes.TradingPlatform.exe** (bridge: `%LocalAppData%\\HermesTrading\\bridge\\`).\n\n"
        + "**Ты можешь:**\n"
        + "- Отвечать на вопросы трейдера: баланс, equity, позиции, ордера, риск, стратегии, логи — по блоку «Trading Platform snapshot» ниже (если есть).\n"
        + "- Объяснять устройство платформы (virtual exchange, risk manager, strategy runner, orchestration layer без прямых ордеров).\n\n"
        + "**Торговые действия из чата** — только JSON (без Markdown), один объект на ответ (не для вопросов «баланс»/«сводка» — на них отвечай текстом):\n"
        + "• **Закрыть позицию** (предпочтительно): `{\"skill\":\"trading\",\"action\":\"close_position\",\"symbol\":\"ETHUSDT\"}` — терминал сам возьмёт размер и сторону (Short→Buy RO, Long→Sell RO). **Не** используй place_order с Buy для закрытия шорта.\n"
        + "• Рыночный/лимит/стоп ордер: `{\"skill\":\"trading\",\"action\":\"place_order\",\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"order_type\":\"Market\",\"quantity\":0.01,\"price\":0,\"reduce_only\":false}`\n"
        + "  (Market: price=0; Limit/Stop: price обязателен; side: Buy|Sell; order_type: Market|Limit|Stop; reduce_only=true только для уменьшения существующей позиции)\n"
        + "• Отмена: `{\"skill\":\"trading\",\"action\":\"cancel_order\",\"order_id\":\"o-1001\"}`\n"
        + "• Алгоритм/стратегия: `{\"skill\":\"trading\",\"action\":\"enable_strategy\",\"strategy_id\":\"momentum\",\"enabled\":true}`\n"
        + "  (id: liq-sweep | momentum | mean-rev)\n"
        + "• Emergency stop: `{\"skill\":\"trading\",\"action\":\"emergency_stop\"}`\n\n"
        + "**Правила:**\n"
        + "- Ордера исполняет **virtual exchange + RiskValidator**, не ты напрямую. При Safe Mode — только reduce-only.\n"
        + "- Если snapshot отсутствует — скажи запустить Hermes.TradingPlatform.exe и включить интеграцию в Settings.\n"
        + "- Этот блок активен только в **режиме трейдинга** (команды «трейдинг» / «trading»). В режиме агента JSON trading не используй.\n"
        + "- Не обещай live Binance execution — только paper/simulation unless user enabled Binance feed in terminal.\n";
}
