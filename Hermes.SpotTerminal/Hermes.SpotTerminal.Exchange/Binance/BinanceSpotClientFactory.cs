using Binance.Net;
using Binance.Net.Clients;
using CryptoExchange.Net.Authentication;

namespace Hermes.SpotTerminal.Exchange.Binance;

/// <summary>
/// Binance Spot Demo Mode (demo-api.binance.com). API keys from Binance → Demo Trading → API Management.
/// </summary>
public static class BinanceSpotClientFactory
{
    public static BinanceRestClient CreateRest(string apiKey, string apiSecret) =>
        new(options =>
        {
            options.Environment = BinanceEnvironment.Demo;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            }
        });

    public static BinanceSocketClient CreateSocket(string apiKey, string apiSecret) =>
        new(options =>
        {
            options.Environment = BinanceEnvironment.Demo;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            }
        });
}
