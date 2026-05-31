using System.Globalization;
using Hermes.BinanceDemoFuturesTerminal.Bridge;
using Hermes.BinanceDemoFuturesTerminal.Models;
using Hermes.BinanceDemoFuturesTerminal.Services;
using Hermes.Terminals.Shared.Bridge;

namespace Hermes.BinanceDemoFuturesTerminal.ViewModels;

public partial class MainViewModel
{
    private FuturesBridgeHost? _bridgeHost;

    private void InitializeBridge()
    {
        _bridgeHost = new FuturesBridgeHost(this);
        _bridgeHost.RequestPublish();
    }

    private void NotifyBridgePublish() => _bridgeHost?.RequestPublish();

    public FuturesTerminalSnapshotSection BuildBridgeSnapshot()
    {
        var settings = AppServices.Settings;
        var currentExposure = Positions.Sum(p => Math.Abs(p.Size) * p.MarkPrice);
        var availableUsdt = Balances
            .FirstOrDefault(b => b.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))?.Free ?? 0;
        var walletUsdt = RiskManager.GetWalletBalanceUsdt(Balances);
        var leverage = EffectiveLeverage;
        var dayPnl = TradeStatsRows.FirstOrDefault(r => r.PeriodLabel == "День")?.RealizedPnl ?? 0;
        return new FuturesTerminalSnapshotSection
        {
            TerminalRunning = true,
            HasCredentials = !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_secretKey),
            SelectedSymbol = SelectedSymbol,
            WsStatus = WsStatus,
            ChartInterval = _selectedChartInterval.ApiInterval,
            LastPrice = (decimal)(Ticker?.LastPrice ?? 0),
            ChangePercent24h = (decimal)(Ticker?.PriceChangePercent ?? 0),
            DefaultAgentOrderUsdt = (decimal)settings.DefaultAgentOrderUsdt,
            MaxOrderMarginPercent = (decimal)settings.MaxOrderMarginPercent,
            MaxOrderNotionalUsdt = (decimal)RiskManager.ComputeMaxOrderNotionalUsdt(walletUsdt, leverage, settings),
            SelectedLeverage = leverage,
            MaxTotalExposureUsdt = (decimal)settings.MaxTotalExposureUsdt,
            CurrentExposureUsdt = (decimal)currentExposure,
            AvailableUsdt = (decimal)availableUsdt,
            WalletBalanceUsdt = (decimal)walletUsdt,
            DailyRealizedPnlUsdt = (decimal)dayPnl,
            MaxOpenPositions = settings.MaxOpenPositions,
            MaxLeverage = settings.MaxLeverage,
            RiskManagementEnabled = settings.RiskManagementEnabled,
            Balances = Balances.Select(b => new FuturesBalanceSnapshot
            {
                Asset = b.Asset,
                Free = (decimal)b.Free,
                Locked = (decimal)b.Locked,
            }).ToList(),
            Positions = Positions.Select(p => new FuturesPositionSnapshot
            {
                Symbol = p.Symbol,
                Side = p.IsLong ? "LONG" : "SHORT",
                Size = (decimal)Math.Abs(p.Size),
                NotionalUsdt = (decimal)(Math.Abs(p.Size) * p.MarkPrice),
                EntryPrice = (decimal)p.EntryPrice,
                MarkPrice = (decimal)p.MarkPrice,
                UnrealizedPnl = (decimal)p.UnrealizedPnl,
                Leverage = p.Leverage,
                MarginType = p.MarginType.ToMarginLabel(),
            }).ToList(),
            OpenOrders = OpenOrders.Select(o =>
            {
                var px = o.Price > 0 ? o.Price : GetOrderPriceEstimate();
                return new FuturesOrderSnapshot
                {
                    Id = o.OrderId.ToString(CultureInfo.InvariantCulture),
                    Symbol = o.Symbol,
                    Side = o.Side,
                    Type = o.Type,
                    Price = (decimal)o.Price,
                    Quantity = (decimal)o.OrigQty,
                    NotionalUsdt = (decimal)(o.OrigQty * px),
                    Status = o.Status,
                    StopPrice = (decimal)o.StopPrice,
                };
            }).ToList(),
        };
    }

    public async Task<FuturesPlatformCommandResultFile> ExecuteBridgeCommandAsync(FuturesPlatformCommand cmd)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
        {
            return Fail(cmd, "API-ключи не настроены. Откройте Настройки в терминале.");
        }

        var result = cmd.Action.ToLowerInvariant() switch
        {
            "place_order" => await ExecuteBridgePlaceOrderAsync(cmd).ConfigureAwait(false),
            "cancel_order" => await ExecuteBridgeCancelOrderAsync(cmd).ConfigureAwait(false),
            "close_position" => await ExecuteBridgeClosePositionAsync(cmd).ConfigureAwait(false),
            "close_all_positions" => await ExecuteBridgeCloseAllPositionsAsync(cmd).ConfigureAwait(false),
            "set_leverage" => await ExecuteBridgeSetLeverageAsync(cmd).ConfigureAwait(false),
            _ => Fail(cmd, $"Unknown action: {cmd.Action}"),
        };

        NotifyBridgePublish();
        return result;
    }

    private async Task<FuturesPlatformCommandResultFile> ExecuteBridgePlaceOrderAsync(FuturesPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Symbol) || string.IsNullOrWhiteSpace(cmd.Side))
        {
            return Fail(cmd, "place_order requires symbol and side");
        }

        var settings = AppServices.Settings;
        var walletBalance = RiskManager.GetWalletBalanceUsdt(Balances);

        if (cmd.QuantityUsdt is null or <= 0 && cmd.Quantity is null or <= 0)
        {
            var leverageForDefault = ResolveLeverageForSymbol(cmd.Symbol!);
            var maxNotional = RiskManager.ComputeMaxOrderNotionalUsdt(walletBalance, leverageForDefault, settings);
            var defaultNotional = Math.Min(
                settings.DefaultAgentOrderUsdt > 0 ? settings.DefaultAgentOrderUsdt : 50,
                maxNotional > 0 && maxNotional < double.MaxValue ? maxNotional : settings.DefaultAgentOrderUsdt);
            cmd.QuantityUsdt = (decimal)Math.Max(defaultNotional, 0);
        }

        var side = cmd.Side.Equals("SELL", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var orderType = (cmd.OrderType ?? "MARKET").Equals("LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT"
            : "MARKET";
        var symbolInfo = _allSymbols.FirstOrDefault(s =>
            s.Symbol.Equals(cmd.Symbol, StringComparison.OrdinalIgnoreCase));

        var price = await GetReferencePriceForSymbolAsync(cmd.Symbol!, cmd.Price is > 0 ? (double?)cmd.Price : null)
            .ConfigureAwait(false);

        string? priceText = null;
        if (orderType == "LIMIT")
        {
            if (cmd.Price is null or <= 0)
            {
                return Fail(cmd, "LIMIT order requires price");
            }

            price = (double)cmd.Price.Value;
            priceText = symbolInfo?.FormatPrice(price) ?? price.ToString(CultureInfo.InvariantCulture);
        }

        var leverage = ResolveLeverageForSymbol(cmd.Symbol!);
        var notionalUsdt = OrderVolumeUsdtHelper.CapNotionalUsdt(
            OrderVolumeUsdtHelper.ResolveNotionalUsdt(
                cmd.QuantityUsdt is > 0 ? (double?)cmd.QuantityUsdt : null,
                cmd.Quantity is > 0 ? (double?)cmd.Quantity : null,
                price,
                settings),
            settings,
            walletBalance,
            leverage);

        if (!OrderVolumeUsdtHelper.TryResolveContracts(symbolInfo, notionalUsdt, price, out _, out var qtyText, out var error))
        {
            return Fail(cmd, error);
        }

        AppServices.Log.Info(OrderVolumeUsdtHelper.FormatOrderLog(side, orderType, cmd.Symbol!, notionalUsdt, priceText));

        var response = await _apiService.PlaceOrderAsync(
            cmd.Symbol,
            side,
            orderType,
            qtyText,
            priceText,
            reduceOnly: cmd.ReduceOnly == true).ConfigureAwait(false);

        await RefreshAccountDataAsync().ConfigureAwait(false);
        var ok = !response.Status.Equals("REJECTED", StringComparison.OrdinalIgnoreCase);
        return new FuturesPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = ok,
            Message = OrderVolumeUsdtHelper.FormatBridgeResult(
                $"Ордер #{response.OrderId} {response.Status} {side} {cmd.Symbol}",
                notionalUsdt,
                ok),
        };
    }

    private async Task<FuturesPlatformCommandResultFile> ExecuteBridgeCancelOrderAsync(FuturesPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Symbol) || string.IsNullOrWhiteSpace(cmd.OrderId))
        {
            return Fail(cmd, "cancel_order requires symbol and order_id");
        }

        if (!long.TryParse(cmd.OrderId, out var orderId))
        {
            return Fail(cmd, "order_id must be numeric");
        }

        var cancelled = await _apiService.CancelOrderAsync(cmd.Symbol, orderId).ConfigureAwait(false);
        await RefreshAccountDataAsync().ConfigureAwait(false);
        AppServices.Log.Info($"Отмена ордера #{orderId} {cmd.Symbol}");
        return new FuturesPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = cancelled is not null,
            Message = cancelled is not null
                ? $"Ордер #{orderId} отменён ({cmd.Symbol})"
                : "Не удалось отменить ордер",
        };
    }

    private async Task<FuturesPlatformCommandResultFile> ExecuteBridgeClosePositionAsync(FuturesPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Symbol))
        {
            return Fail(cmd, "close_position requires symbol");
        }

        var position = Positions.FirstOrDefault(p =>
            p.Symbol.Equals(cmd.Symbol, StringComparison.OrdinalIgnoreCase));
        if (position is null)
        {
            return Fail(cmd, $"No open position for {cmd.Symbol}");
        }

        var symbolInfo = _allSymbols.FirstOrDefault(s =>
            s.Symbol.Equals(cmd.Symbol, StringComparison.OrdinalIgnoreCase));
        var markPrice = position.MarkPrice > 0 ? position.MarkPrice : GetOrderPriceEstimate();
        var fullNotional = Math.Abs(position.Size) * markPrice;

        double notionalUsdt;
        double qty;
        if (cmd.QuantityUsdt is > 0)
        {
            notionalUsdt = (double)cmd.QuantityUsdt.Value;
            qty = markPrice > 0 ? notionalUsdt / markPrice : Math.Abs(position.Size);
        }
        else if (cmd.Quantity is > 0)
        {
            qty = (double)cmd.Quantity.Value;
            notionalUsdt = qty * markPrice;
        }
        else
        {
            qty = Math.Abs(position.Size);
            notionalUsdt = fullNotional;
        }

        qty = Math.Min(qty, Math.Abs(position.Size));
        notionalUsdt = qty * markPrice;

        var qtyText = symbolInfo?.FormatQuantity(qty)
                      ?? qty.ToString(CultureInfo.InvariantCulture);
        var side = position.IsLong ? "SELL" : "BUY";
        var orderType = (cmd.OrderType ?? "MARKET").Equals("LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT"
            : "MARKET";

        string? priceText = null;
        if (orderType == "LIMIT")
        {
            var price = cmd.Price is > 0 ? (double)cmd.Price.Value : markPrice;
            priceText = symbolInfo?.FormatPrice(price) ?? price.ToString(CultureInfo.InvariantCulture);
        }

        AppServices.Log.Info(OrderVolumeUsdtHelper.FormatOrderLog(side, orderType, cmd.Symbol!, notionalUsdt, priceText));

        var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 2000;
        var response = await _apiService.PlaceOrderAsync(
            cmd.Symbol,
            side,
            orderType,
            qtyText,
            priceText,
            reduceOnly: true).ConfigureAwait(false);

        await RefreshAccountDataAsync().ConfigureAwait(false);
        var ok = !response.Status.Equals("REJECTED", StringComparison.OrdinalIgnoreCase);
        double? realizedPnl = null;
        if (ok)
        {
            realizedPnl = await CloseRealizedPnlPoller
                .PollOrderPnlAsync(_apiService, cmd.Symbol!, response.OrderId, startMs)
                .ConfigureAwait(false);
        }

        return new FuturesPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = ok,
            RealizedPnlUsdt = realizedPnl.HasValue ? (decimal)realizedPnl.Value : null,
            Message = OrderVolumeUsdtHelper.FormatCloseResult(
                $"Закрытие {cmd.Symbol}: ордер #{response.OrderId} {response.Status}",
                notionalUsdt,
                realizedPnl,
                ok),
        };
    }

    private async Task<FuturesPlatformCommandResultFile> ExecuteBridgeCloseAllPositionsAsync(FuturesPlatformCommand cmd)
    {
        var positions = Positions.ToList();
        if (positions.Count == 0)
        {
            return new FuturesPlatformCommandResultFile
            {
                CommandId = cmd.Id,
                Success = true,
                Message = "Нет открытых позиций",
            };
        }

        var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 2000;
        var closed = 0;
        var totalNotional = 0.0;
        foreach (var position in positions)
        {
            var symbolInfo = _allSymbols.FirstOrDefault(s =>
                s.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase));
            var markPrice = position.MarkPrice > 0 ? position.MarkPrice : 0;
            var notional = Math.Abs(position.Size) * markPrice;
            totalNotional += notional;

            var qtyText = symbolInfo?.FormatQuantity(Math.Abs(position.Size))
                          ?? Math.Abs(position.Size).ToString(CultureInfo.InvariantCulture);
            var side = position.IsLong ? "SELL" : "BUY";
            AppServices.Log.Info(OrderVolumeUsdtHelper.FormatOrderLog(side, "MARKET", position.Symbol, notional));
            await _apiService.PlaceOrderAsync(
                position.Symbol,
                side,
                "MARKET",
                qtyText,
                reduceOnly: true).ConfigureAwait(false);
            closed++;
        }

        await RefreshAccountDataAsync().ConfigureAwait(false);
        var symbols = positions.Select(p => p.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pnlBySymbol = await CloseRealizedPnlPoller
            .PollMultiSymbolPnlAsync(_apiService, symbols, startMs)
            .ConfigureAwait(false);
        var totalPnl = pnlBySymbol.Values.Sum();
        var pnlDetails = pnlBySymbol.Count > 0
            ? string.Join(", ", pnlBySymbol.Select(kv => $"{kv.Key}: {OrderVolumeUsdtHelper.FormatSignedPnl(kv.Value)}"))
            : null;

        return new FuturesPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = true,
            RealizedPnlUsdt = pnlBySymbol.Count > 0 ? (decimal)totalPnl : null,
            Message = OrderVolumeUsdtHelper.FormatCloseResult(
                $"Закрыто позиций: {closed}",
                totalNotional,
                pnlBySymbol.Count > 0 ? totalPnl : null,
                true,
                pnlDetails),
        };
    }

    private async Task<FuturesPlatformCommandResultFile> ExecuteBridgeSetLeverageAsync(FuturesPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Symbol) || cmd.Leverage is null or < 1)
        {
            return Fail(cmd, "set_leverage requires symbol and leverage");
        }

        var ok = await _apiService.SetLeverageAsync(cmd.Symbol, cmd.Leverage.Value).ConfigureAwait(false);
        await RefreshAccountDataAsync().ConfigureAwait(false);
        return new FuturesPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = ok,
            Message = ok ? $"Плечо {cmd.Leverage}x установлено для {cmd.Symbol}" : "Не удалось установить плечо",
        };
    }

    private async Task<double> GetReferencePriceForSymbolAsync(string symbol, double? limitPrice = null)
    {
        if (limitPrice is > 0)
        {
            return limitPrice.Value;
        }

        if (symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase))
        {
            var estimate = GetOrderPriceEstimate();
            if (estimate > 0)
            {
                return estimate;
            }
        }

        var candles = await _apiService.GetKlinesAsync(symbol, "1m", 1).ConfigureAwait(false);
        return candles.Count > 0 ? candles[^1].Close : 0;
    }

    private int ResolveLeverageForSymbol(string symbol)
    {
        var position = Positions.FirstOrDefault(p =>
            p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (position is not null && position.Leverage > 0)
        {
            return RiskManager.CapLeverage(position.Leverage, AppServices.Settings.MaxLeverage);
        }

        if (symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return EffectiveLeverage;
        }

        return RiskManager.CapLeverage(AppServices.Settings.DefaultLeverage, AppServices.Settings.MaxLeverage);
    }

    private static FuturesPlatformCommandResultFile Fail(FuturesPlatformCommand cmd, string message) =>
        new() { CommandId = cmd.Id, Success = false, Message = message };
}
