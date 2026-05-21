namespace Hermes.TradingPlatform.Exchange;

internal readonly record struct PositionFillImpact(decimal RealizedPnl, string JournalKind);
