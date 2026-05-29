using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;

namespace Hermes.SpotTerminal.Data.Seed;

public static class InitialSpotSeed
{
    public static SpotPlatformState Create()
    {
        var state = new SpotPlatformState
        {
            Mode = ExecutionMode.Virtual,
            FeedStatus = "Disconnected",
            Agent = new AgentSession { State = "Idle", CurrentThought = "Awaiting market data." },
        };

        state.Balances.AddRange([
            new SpotBalance { Asset = "USDT", Free = 10_000m, Locked = 0m },
            new SpotBalance { Asset = "BTC", Free = 0m, Locked = 0m },
            new SpotBalance { Asset = "ETH", Free = 0m, Locked = 0m },
        ]);

        foreach (var sym in new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" })
        {
            state.Tickers.Add(new MarketTicker
            {
                Symbol = sym,
                Price = sym.StartsWith("BTC") ? 67_000m : sym.StartsWith("ETH") ? 3_500m : 150m,
                ChangePercent24h = 0m,
                Volume24h = 0m,
                InWatchlist = true,
            });
        }

        state.Skills.AddRange([
            new Skill
            {
                Id = "momentum-spot", Name = "Spot Momentum", Description = "Buy strength on 24h gain.",
                Status = SkillStatus.Approved, IsInitial = true, CreatedAtUtc = DateTimeOffset.UtcNow,
                ApprovedAtUtc = DateTimeOffset.UtcNow,
            },
            new Skill
            {
                Id = "mean-revert-spot", Name = "Spot Mean Reversion", Description = "Fade extremes.",
                Status = SkillStatus.Draft, IsInitial = true, CreatedAtUtc = DateTimeOffset.UtcNow,
            },
        ]);

        return state;
    }
}
