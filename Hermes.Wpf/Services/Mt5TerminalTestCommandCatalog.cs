namespace Hermes.Wpf.Services;

/// <summary>Human-language presets for Mt5Terminal agent chat (same as typing in chat).</summary>
public static class Mt5TerminalTestCommandCatalog
{
    public sealed record Item(string Title, string Prompt, string Hint);

    public static IReadOnlyList<Item> All { get; } = new[]
    {
        new Item(
            "Скриншот графика",
            "Сделай скриншот графика",
            "→ screenshot → ChartScreenShot → отчёт + ссылка в чате"),
        new Item(
            "Статус и позиции",
            "Покажи статус терминала и открытые позиции",
            "→ snapshot → факты из HWT (позиции, real trading, log_tail)"),
        new Item(
            "Баланс счёта",
            "Какой сейчас баланс счёта?",
            "→ snapshot → поле account из HWT/MT5"),
        new Item(
            "Цена инструмента",
            "Цена активного инструмента?",
            "→ snapshot → symbol + bid/ask активного графика"),
        new Item(
            "Включи Real trading",
            "Включи real trading",
            "→ set_real_trading true"),
        new Item(
            "Выключи Real trading",
            "Выключи real trading",
            "→ set_real_trading false"),
        new Item(
            "Лонг по маркету",
            "Открой лонг по маркету лотом 0.01",
            "→ buy_market (нужен Real trading)"),
        new Item(
            "Шорт по маркету",
            "Открой шорт по маркету лотом 0.01",
            "→ sell_market (нужен Real trading)"),
        new Item(
            "Закрой все позиции",
            "Закрой все позиции",
            "→ close_all"),
        new Item(
            "Закрой слот 0",
            "Закрой позицию в слоте 0",
            "→ close_slot 0"),
        new Item(
            "Лот 0.01",
            "Установи лот 0.01",
            "→ set_lot"),
        new Item(
            "Обнови RemoteTerminal",
            "Обнови RemoteTerminal",
            "→ refresh (публикация hwt_status)"),
        new Item(
            "Список символов MT5",
            "Получи список символов MT5",
            "→ list_symbols"),
        new Item(
            "Sell Limit XAUUSD (тест)",
            "Поставь Sell Limit по XAUUSD: цена 4490, стоп 4505, тейк 4450, лот 0.01",
            "→ place_pending"),
    };
}
