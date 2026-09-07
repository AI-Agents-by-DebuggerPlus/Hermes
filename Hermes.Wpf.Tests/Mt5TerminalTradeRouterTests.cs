using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public class Mt5TerminalTradeRouterTests
{
    [Fact]
    public void Parses_whitelist_close_all()
    {
        var cmd = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "ok\n```json\n{\"action\":\"close_all\",\"id\":\"abc\"}\n```\n");
        Assert.NotNull(cmd);
        Assert.Equal("close_all", cmd!.Action);
        Assert.Equal("abc", cmd.Id);
        Assert.False(cmd.IsUnsupported);
    }

    [Fact]
    public void Parses_unsupported_with_reason()
    {
        var cmd = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"action\":\"unsupported\",\"reason\":\"Нет задачи для хеджа\"}");
        Assert.NotNull(cmd);
        Assert.True(cmd!.IsUnsupported);
        Assert.Contains("хедж", cmd.Reason);
    }

    [Fact]
    public void Rejects_unknown_action()
    {
        var cmd = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"action\":\"hack_broker\",\"id\":\"x\"}");
        Assert.Null(cmd);
    }

    [Fact]
    public void Parses_envelope_and_lot()
    {
        var cmd = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"hermes_wpf_terminal\":{\"action\":\"buy_market\",\"lot\":0.01,\"id\":\"b1\"}}");
        Assert.NotNull(cmd);
        Assert.Equal("buy_market", cmd!.Action);
        Assert.Equal(0.01, cmd.Lot);
    }

    [Fact]
    public void Parses_screenshot_action()
    {
        var cmd = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"action\":\"screenshot\",\"id\":\"shot1\"}");
        Assert.NotNull(cmd);
        Assert.Equal("screenshot", cmd!.Action);
        Assert.Equal("shot1", cmd.Id);
    }

    [Fact]
    public void Parses_refresh_action()
    {
        var cmd = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"action\":\"refresh\",\"id\":\"r1\"}");
        Assert.NotNull(cmd);
        Assert.Equal("refresh", cmd!.Action);
    }

    [Fact]
    public void Parses_place_pending_with_prices()
    {
        var cmd = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            """
            {"action":"place_pending","id":"p1","symbol":"XAUUSD","order_type_label":"Buy Limit","price":4370,"stop_loss":4345,"take_profit":4500,"lot":0.01}
            """);
        Assert.NotNull(cmd);
        Assert.Equal("place_pending", cmd!.Action);
        Assert.Equal("Buy Limit", cmd.PendingOrderType);
        Assert.Equal(4370, cmd.Price);
    }

    [Fact]
    public void Parses_list_symbols_action()
    {
        var cmd = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"action\":\"list_symbols\",\"id\":\"sym1\"}");
        Assert.NotNull(cmd);
        Assert.Equal("list_symbols", cmd!.Action);
    }

    [Fact]
    public void Parses_mt5_symbols_fetch_skill()
    {
        Assert.True(TradingAnalyticsMt5SymbolsParser.TryParseFetchRequest(
            "ok\n```json\n{\"skill\":\"mt5_symbols\",\"action\":\"fetch\"}\n```",
            out var force));
        Assert.True(force);
    }

    [Fact]
    public void Parses_mt5_symbols_catalog_json()
    {
        const string json = """
            {"source":"Mt5Terminal","utc":"2026-08-19","chart_symbol":"USDJPY","count":2,"symbols":["EURUSD","XAUUSD"]}
            """;
        var cat = Mt5TerminalSymbolsService.ParseCatalog(json);
        Assert.NotNull(cat);
        Assert.Equal(2, cat!.Count);
        Assert.Contains("XAUUSD", cat.Symbols);
    }

    [Fact]
    public void Corrects_to_refresh_for_refresh_intent()
    {
        var bad = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"action\":\"snapshot\",\"id\":\"x\"}");
        Assert.NotNull(bad);
        var fixedRoute = Mt5TerminalTradeRouter.CorrectRouteForUserIntent(bad!, "refresh");
        Assert.Equal("refresh", fixedRoute.Action);
    }

    [Fact]
    public void Corrects_snapshot_to_screenshot_for_skrinshot_intent()
    {
        var bad = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"action\":\"snapshot\",\"id\":\"x\"}");
        Assert.NotNull(bad);
        var fixedRoute = Mt5TerminalTradeRouter.CorrectRouteForUserIntent(bad!, "скриншот");
        Assert.Equal("screenshot", fixedRoute.Action);
        Assert.Equal("x", fixedRoute.Id);
    }

    [Fact]
    public void LooksLikeChartScreenshotRequest_exact()
    {
        Assert.True(Mt5TerminalTradeRouter.LooksLikeChartScreenshotRequest("скриншот"));
        Assert.True(Mt5TerminalTradeRouter.LooksLikeChartScreenshotRequest("screenshot"));
        Assert.True(Mt5TerminalTradeRouter.LooksLikeChartScreenshotRequest("screeshot"));
        Assert.True(Mt5TerminalTradeRouter.LooksLikeChartScreenshotRequest("Screenshot"));
        Assert.False(Mt5TerminalTradeRouter.LooksLikeChartScreenshotRequest("закрой все позиции"));
    }

    [Fact]
    public void Corrects_snapshot_to_screenshot_for_screeshot_typo()
    {
        var bad = Mt5TerminalTradeRouter.TryParseFromAgentOutput(
            "{\"action\":\"snapshot\",\"id\":\"x\"}");
        Assert.NotNull(bad);
        var fixedRoute = Mt5TerminalTradeRouter.CorrectRouteForUserIntent(bad!, "screeshot");
        Assert.Equal("screenshot", fixedRoute.Action);
    }

    [Fact]
    public void LooksLikeRepeatRequest_exact()
    {
        Assert.True(Mt5TerminalTradeRouter.LooksLikeRepeatRequest("повтор"));
        Assert.True(Mt5TerminalTradeRouter.LooksLikeRepeatRequest("repeat"));
        Assert.True(Mt5TerminalTradeRouter.LooksLikeRepeatRequest("Repeat"));
        Assert.False(Mt5TerminalTradeRouter.LooksLikeRepeatRequest("screenshot"));
        Assert.False(Mt5TerminalTradeRouter.LooksLikeRepeatRequest("refresh"));
    }

    [Fact]
    public void IsMt5TerminalProject_name()
    {
        Assert.True(Mt5TerminalTradeRouter.IsMt5TerminalProject("Mt5Terminal"));
        Assert.False(Mt5TerminalTradeRouter.IsMt5TerminalProject("Utilities"));
    }
}
