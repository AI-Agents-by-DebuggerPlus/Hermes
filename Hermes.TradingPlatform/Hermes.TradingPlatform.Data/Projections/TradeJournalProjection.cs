using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;
using Hermes.TradingPlatform.Data.Persistence;

namespace Hermes.TradingPlatform.Data.Projections;

public sealed class TradeJournalProjection
{
    private const int MaxJournalEntries = 500;
    private readonly ITradingStateStore _store;
    private readonly IEventBus _bus;
    private readonly TradeJournalFileWriter _fileWriter;

    public TradeJournalProjection(ITradingStateStore store, IEventBus bus, TradeJournalFileWriter? fileWriter = null)
    {
        _store = store;
        _bus = bus;
        _fileWriter = fileWriter ?? new TradeJournalFileWriter();
        _bus.Subscribe<OrderFilledEvent>(OnOrderFilled);
    }

    private void OnOrderFilled(OrderFilledEvent filled)
    {
        var order = filled.Order;
        var entry = new TradeJournalEntry
        {
            Timestamp = filled.OccurredAt,
            OrderId = order.Id,
            Symbol = order.Symbol,
            Kind = filled.JournalKind,
            Side = order.Side.ToString(),
            Quantity = order.Quantity,
            FillPrice = filled.FillPrice,
            Fee = filled.Fee,
            RealizedPnl = filled.RealizedPnl,
            BalanceBefore = filled.BalanceBefore,
            BalanceAfter = filled.BalanceAfter,
            ReduceOnly = order.ReduceOnly,
        };

        _store.Mutate(s =>
        {
            s.Journal.Insert(0, entry);
            if (s.Journal.Count > MaxJournalEntries)
            {
                s.Journal.RemoveRange(MaxJournalEntries, s.Journal.Count - MaxJournalEntries);
            }
        });

        _fileWriter.Append(entry);
    }
}
