using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public sealed class TradingAnalyticsSignalParserTests
{
    private const string Sample = """
        [info]
        Analysis text here.

        ```json
        {
          "skill": "trade_signal",
          "signal_id": "sig1",
          "symbol": "XAUUSD",
          "side": "buy",
          "order_type": "limit",
          "entry": 4370,
          "entry_high": 4375,
          "stop_loss": 4345,
          "take_profit": 4500,
          "lot": 0.01,
          "timeframe": "H1",
          "summary": "Swing buy on correction"
        }
        ```
        """;

    [Fact]
    public void Parses_trade_signal_json()
    {
        Assert.True(TradingAnalyticsSignalParser.TryParseFromAgentOutput(Sample, out var card));
        Assert.Equal("XAUUSD", card.Symbol);
        Assert.Equal("Buy Limit", card.PendingOrderTypeLabel);
        Assert.Equal(4372.5, card.EffectiveEntry);
        Assert.Equal(0.01, card.Lot);
    }

    [Fact]
    public void Strips_json_from_display()
    {
        var stripped = TradingAnalyticsSignalParser.StripSignalJsonFromDisplay(Sample);
        Assert.DoesNotContain("trade_signal", stripped);
        Assert.Contains("Analysis text", stripped);
    }

    [Fact]
    public void Whitelist_allows_xau_variants()
    {
        Assert.True(Mt5TerminalInstrumentWhitelist.IsAllowed("XAU/USD"));
        Assert.True(Mt5TerminalInstrumentWhitelist.MatchesChartSymbol("XAUUSD", "XAUUSDm"));
    }

    [Fact]
    public void Executor_builds_place_pending_command()
    {
        var card = new TradingAnalyticsSignalCard
        {
            SignalId = "a",
            Symbol = "XAUUSD",
            Side = "buy",
            OrderType = "limit",
            Entry = 4370,
            StopLoss = 4345,
            TakeProfit = 4500,
            Lot = 0.01,
        };
        var cmd = TradingAnalyticsSignalExecutor.ToMt5Command(card);
        Assert.Equal("place_pending", cmd.Action);
        Assert.Equal("Buy Limit", cmd.PendingOrderType);
        Assert.Equal(4370, cmd.Price);
    }

    [Fact]
    public void Parses_markdown_signal_without_json()
    {
        const string md = """
            ### Тестовый сигнал: XAUUSD (Gold)

            *   **Тип ордера:** Sell Limit (отложенный ордер)
            *   **Инструмент:** XAUUSD
            *   **Лот:** 0.01
            *   **Цена входа (Entry):** **$4,370.00**
            *   **Стоп-лосс (SL):** **$4,385.00**
            *   **Тейк-профит (TP):** **$4,320.00**
            """;
        Assert.True(TradingAnalyticsSignalParser.TryParseFromStructuredText(md, out var card));
        Assert.Equal("XAUUSD", card.Symbol);
        Assert.Equal("Sell Limit", card.PendingOrderTypeLabel);
        Assert.Equal(4370, card.Entry);
        Assert.Equal(4385, card.StopLoss);
        Assert.Equal(4320, card.TakeProfit);
        Assert.Equal(0.01, card.Lot);
    }

    [Fact]
    public void Parses_nas100_from_heading_not_truncated()
    {
        const string md = """
            ### Тестовый сигнал: NAS100 (Nasdaq)

            *   **Тип ордера:** Buy Limit (отложенный ордер на покупку)
            *   **Инструмент:** NAS100 (или USTEC / NASDAQ в зависимости от брокера)
            *   **Лот:** 0.01
            *   **Цена входа (Entry):** **19,750.00**
            *   **Стоп-лосс (SL):** **19,680.00**
            *   **Тейк-профит (TP):** **19,950.00**
            """;
        Assert.True(TradingAnalyticsSignalParser.TryParseFromStructuredText(md, out var card));
        Assert.Equal("NAS100", card.Symbol);
        Assert.True(Mt5TerminalInstrumentWhitelist.IsAllowed(card.Symbol));
    }

    [Fact]
    public void Ignores_mt5_word_in_install_steps()
    {
        const string md = """
            ### Тестовый сигнал: XAUUSD (Gold)

            *   **Тип ордера:** Sell Limit (отложенный ордер на продажу)
            *   **Инструмент:** XAUUSD
            *   **Лот:** **0.01**
            *   **Цена входа (Entry):** **4370.00**
            *   **Стоп-лосс (SL):** **4382.00**
            *   **Тейк-профит (TP):** **4330.00**

            ### Шаги для установки в MT5:
            1.  Открой окно **"New Order"** (F9).
            2.  Выбери **Symbol:** `XAUUSD`.
            """;
        Assert.True(TradingAnalyticsSignalParser.TryParseFromStructuredText(md, out var card));
        Assert.Equal("XAUUSD", card.Symbol);
        Assert.Equal("Sell Limit", card.PendingOrderTypeLabel);
    }

    [Fact]
    public void TryAddUserSymbol_enables_custom_symbol()
    {
        const string sym = "HERMES_UNIT_TEST_SYM";
        Assert.True(Mt5TerminalInstrumentWhitelist.TryAddUserSymbol(sym, out var norm));
        Assert.Equal(sym, norm);
        Assert.True(Mt5TerminalInstrumentWhitelist.IsAllowed(sym));
    }
}
