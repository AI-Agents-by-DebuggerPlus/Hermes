using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public sealed class RoleAwareMemoryRouterTests
{
    private static MemoryItem Item(
        string[] tags,
        string path,
        int importance = 3) =>
        new()
        {
            Content = $"content {Path.GetFileName(path)}",
            Tags = tags.ToList(),
            SourceFile = path,
            Importance = importance,
            Timestamp = DateTime.UtcNow,
            Type = "semantic",
        };

    [Fact]
    public void FilterAndBoost_prioritizes_trading_tags_in_trader_role()
    {
        var router = new RoleAwareMemoryRouter { CurrentRole = AgentRole.Trader };
        var items = new[]
        {
            Item(["english", "vocabulary"], @"D:\vault\Knowledge\English\lesson.md"),
            Item(["trading", "risk"], @"D:\vault\Knowledge\Trading\stop.md"),
        };

        var result = router.FilterAndBoost(items, "risk position trading", 2);
        Assert.Contains("Trading", result[0].SourceFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterAndBoost_penalizes_foreign_role_tags()
    {
        var router = new RoleAwareMemoryRouter { CurrentRole = AgentRole.Developer };
        var items = new[]
        {
            Item(["trading", "pnl"], @"D:\vault\Knowledge\Trading\x.md", 5),
            Item(["dotnet", "csharp"], @"D:\vault\Knowledge\Development\api.md", 3),
        };

        var result = router.FilterAndBoost(items, "csharp wpf", 2);
        Assert.Contains("Development", result[0].SourceFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterAndBoost_universal_uses_lexical_only()
    {
        var router = new RoleAwareMemoryRouter { CurrentRole = AgentRole.Universal };
        var items = new[]
        {
            Item(["alpha"], @"D:\vault\Knowledge\alpha.md"),
            Item(["beta"], @"D:\vault\Knowledge\beta.md"),
        };

        var result = router.FilterAndBoost(items, "alpha", 1);
        Assert.Single(result);
        Assert.Contains("alpha", result[0].SourceFile, StringComparison.OrdinalIgnoreCase);
    }
}
