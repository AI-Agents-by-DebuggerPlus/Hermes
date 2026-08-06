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
    public void IsMt5TerminalProject_name()
    {
        Assert.True(Mt5TerminalTradeRouter.IsMt5TerminalProject("Mt5Terminal"));
        Assert.False(Mt5TerminalTradeRouter.IsMt5TerminalProject("Utilities"));
    }
}
