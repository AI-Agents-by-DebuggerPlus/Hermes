using Hermes.TradingPlatform.Core.Abstractions;

namespace Hermes.TradingPlatform.Exchange;

public sealed class ExchangeModule : IPlatformModule
{
    public string Name => "VirtualExchange";
}
