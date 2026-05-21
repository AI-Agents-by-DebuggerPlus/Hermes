using Hermes.TradingPlatform.Core.Abstractions;

namespace Hermes.TradingPlatform.Exchange;

/// <summary>Phase 3: virtual exchange core. UI-only in Phase 1.</summary>
public sealed class ExchangeModulePlaceholder : IPlatformModule
{
    public string Name => "VirtualExchange";
}
