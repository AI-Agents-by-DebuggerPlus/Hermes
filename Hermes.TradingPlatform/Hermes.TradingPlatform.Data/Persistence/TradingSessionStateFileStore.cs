using System.Text.Json;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.State;
using Hermes.TradingPlatform.Data.Seed;

namespace Hermes.TradingPlatform.Data.Persistence;

public sealed class TradingSessionStateFileStore
{
    private const int MaxOrders = 500;
    private const int MaxJournal = 1000;
    private const int MaxLogs = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public TradingSessionStateFileStore(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesTrading");
        Directory.CreateDirectory(dir);
        FilePath = filePath ?? Path.Combine(dir, "session-state.json");
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public void Save(TradingPlatformState state, int nextOrderSequence)
    {
        var file = MapToFile(state, nextOrderSequence);
        file.SavedAtUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(file, JsonOptions);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, null);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }

    public TradingPlatformState LoadOrSeed()
    {
        if (!TryLoad(out var state, out _))
        {
            state = InitialTradingSeed.Create();
        }

        TradingStateCalculator.RecalculateEquity(state);
        return state;
    }

    public bool TryLoad(out TradingPlatformState state, out int nextOrderSequence)
    {
        state = new TradingPlatformState();
        nextOrderSequence = 1004;

        if (!File.Exists(FilePath))
        {
            return false;
        }

        try
        {
            var file = JsonSerializer.Deserialize<TradingSessionStateFile>(File.ReadAllText(FilePath), JsonOptions);
            if (file is null)
            {
                return false;
            }

            ApplyFile(state, file);
            nextOrderSequence = Math.Max(file.NextOrderSequence, InferNextOrderSequence(file.Orders));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int InferNextOrderSequence(IEnumerable<OrderFileModel> orders)
    {
        var max = 1003;
        foreach (var o in orders)
        {
            if (o.Id.StartsWith("o-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(o.Id.AsSpan(2), out var n))
            {
                max = Math.Max(max, n);
            }
        }

        return max + 1;
    }

    private static TradingSessionStateFile MapToFile(TradingPlatformState s, int nextOrderSequence) =>
        new()
        {
            NextOrderSequence = nextOrderSequence,
            Account = new AccountFileModel
            {
                Balance = s.Account.Balance,
                Equity = s.Account.Equity,
                FreeMargin = s.Account.FreeMargin,
                UsedMargin = s.Account.UsedMargin,
                Leverage = s.Account.Leverage,
            },
            Pnl = new PnlFileModel
            {
                Today = s.Pnl.Today,
                Week = s.Pnl.Week,
                Month = s.Pnl.Month,
                AllTime = s.Pnl.AllTime,
            },
            Positions = s.Positions.Select(p => new PositionFileModel
            {
                Symbol = p.Symbol,
                Side = p.Side.ToString(),
                Size = p.Size,
                EntryPrice = p.EntryPrice,
                MarkPrice = p.MarkPrice,
                UnrealizedPnl = p.UnrealizedPnl,
                RealizedPnl = p.RealizedPnl,
                LiquidationPrice = p.LiquidationPrice,
            }).ToList(),
            Orders = s.Orders.Take(MaxOrders).Select(o => new OrderFileModel
            {
                Id = o.Id,
                Symbol = o.Symbol,
                Type = o.Type.ToString(),
                Side = o.Side.ToString(),
                Price = o.Price,
                TriggerPrice = o.TriggerPrice,
                Quantity = o.Quantity,
                Status = o.Status.ToString(),
                ReduceOnly = o.ReduceOnly,
                CreatedAt = o.CreatedAt,
            }).ToList(),
            Tickers = s.Tickers.Select(t => new TickerFileModel
            {
                Symbol = t.Symbol,
                Price = t.Price,
                ChangePercent24h = t.ChangePercent24h,
                Volume24h = t.Volume24h,
                InWatchlist = t.InWatchlist,
            }).ToList(),
            Strategies = s.Strategies.Select(st => new StrategyFileModel
            {
                Id = st.Id,
                Name = st.Name,
                Description = st.Description,
                RiskProfileLabel = st.RiskProfileLabel,
                Status = st.Status.ToString(),
                IsEnabled = st.IsEnabled,
            }).ToList(),
            Journal = s.Journal.Take(MaxJournal).Select(j => new JournalFileModel
            {
                Id = j.Id,
                Timestamp = j.Timestamp,
                OrderId = j.OrderId,
                Symbol = j.Symbol,
                Kind = j.Kind,
                Side = j.Side,
                Quantity = j.Quantity,
                FillPrice = j.FillPrice,
                Fee = j.Fee,
                RealizedPnl = j.RealizedPnl,
                BalanceBefore = j.BalanceBefore,
                BalanceAfter = j.BalanceAfter,
                ReduceOnly = j.ReduceOnly,
            }).ToList(),
            Logs = s.Logs.Take(MaxLogs).Select(l => new LogFileModel
            {
                Timestamp = l.Timestamp,
                EventType = l.EventType,
                Source = l.Source,
                Message = l.Message,
            }).ToList(),
        };

    private static void ApplyFile(TradingPlatformState state, TradingSessionStateFile file)
    {
        state.Account.Balance = file.Account.Balance;
        state.Account.Equity = file.Account.Equity;
        state.Account.FreeMargin = file.Account.FreeMargin;
        state.Account.UsedMargin = file.Account.UsedMargin;
        state.Account.Leverage = file.Account.Leverage;

        state.Pnl.Today = file.Pnl.Today;
        state.Pnl.Week = file.Pnl.Week;
        state.Pnl.Month = file.Pnl.Month;
        state.Pnl.AllTime = file.Pnl.AllTime;

        foreach (var p in file.Positions)
        {
            state.Positions.Add(new Position
            {
                Symbol = p.Symbol,
                Side = Enum.Parse<PositionSide>(p.Side, true),
                Size = p.Size,
                EntryPrice = p.EntryPrice,
                MarkPrice = p.MarkPrice,
                UnrealizedPnl = p.UnrealizedPnl,
                RealizedPnl = p.RealizedPnl,
                LiquidationPrice = p.LiquidationPrice,
            });
        }

        foreach (var o in file.Orders)
        {
            state.Orders.Add(new Order
            {
                Id = o.Id,
                Symbol = o.Symbol,
                Type = Enum.Parse<OrderType>(o.Type, true),
                Side = Enum.Parse<OrderSide>(o.Side, true),
                Price = o.Price,
                TriggerPrice = o.TriggerPrice,
                Quantity = o.Quantity,
                Status = Enum.Parse<OrderStatus>(o.Status, true),
                ReduceOnly = o.ReduceOnly,
                CreatedAt = o.CreatedAt,
            });
        }

        foreach (var t in file.Tickers)
        {
            state.Tickers.Add(new MarketTicker
            {
                Symbol = t.Symbol,
                Price = t.Price,
                ChangePercent24h = t.ChangePercent24h,
                Volume24h = t.Volume24h,
                InWatchlist = t.InWatchlist,
            });
        }

        foreach (var st in file.Strategies)
        {
            state.Strategies.Add(new StrategyState
            {
                Id = st.Id,
                Name = st.Name,
                Description = st.Description,
                RiskProfileLabel = st.RiskProfileLabel,
                Status = Enum.Parse<StrategyRunStatus>(st.Status, true),
                IsEnabled = st.IsEnabled,
            });
        }

        foreach (var j in file.Journal)
        {
            state.Journal.Add(new TradeJournalEntry
            {
                Id = j.Id,
                Timestamp = j.Timestamp,
                OrderId = j.OrderId,
                Symbol = j.Symbol,
                Kind = j.Kind,
                Side = j.Side,
                Quantity = j.Quantity,
                FillPrice = j.FillPrice,
                Fee = j.Fee,
                RealizedPnl = j.RealizedPnl,
                BalanceBefore = j.BalanceBefore,
                BalanceAfter = j.BalanceAfter,
                ReduceOnly = j.ReduceOnly,
            });
        }

        foreach (var l in file.Logs)
        {
            state.Logs.Add(new PlatformLogEntry
            {
                Timestamp = l.Timestamp,
                EventType = l.EventType,
                Source = l.Source,
                Message = l.Message,
            });
        }

        MergeMissingSeedTickersAndStrategies(state);
    }

    private static void MergeMissingSeedTickersAndStrategies(TradingPlatformState state)
    {
        var seed = InitialTradingSeed.Create();
        foreach (var t in seed.Tickers)
        {
            if (state.Tickers.All(x => x.Symbol != t.Symbol))
            {
                state.Tickers.Add(new MarketTicker
                {
                    Symbol = t.Symbol,
                    Price = t.Price,
                    ChangePercent24h = t.ChangePercent24h,
                    Volume24h = t.Volume24h,
                    InWatchlist = t.InWatchlist,
                });
            }
        }

        foreach (var s in seed.Strategies)
        {
            if (state.Strategies.All(x => x.Id != s.Id))
            {
                state.Strategies.Add(new StrategyState
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    RiskProfileLabel = s.RiskProfileLabel,
                    Status = s.Status,
                    IsEnabled = s.IsEnabled,
                });
            }
        }
    }
}
