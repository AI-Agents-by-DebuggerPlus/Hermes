using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

/// <summary>Local chat flow: open long/short in plain Russian → futures (or spot) bridge.</summary>
internal sealed class TradingManualOrderHandler
{
    private readonly FuturesTerminalBridgeService _futures;
    private readonly SpotTerminalBridgeService _spot;
    private readonly LogService _log;
    private ManualOrderDraft? _pending;

    public TradingManualOrderHandler(
        FuturesTerminalBridgeService futures,
        SpotTerminalBridgeService spot,
        LogService log)
    {
        _futures = futures;
        _spot = spot;
        _log = log;
    }

    public void ClearPending() => _pending = null;

    public async Task<bool> TryHandleAsync(
        string payload,
        string projectName,
        bool tradingModeEnabled,
        Func<string, string, Task> postHermesReplyAsync,
        Func<string, string, Task> appendSystemLogAsync)
    {
        if (!tradingModeEnabled || (!_futures.IsActiveForSession && !_spot.IsActiveForSession))
        {
            return false;
        }

        var text = (payload ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (_pending is not null)
        {
            return await TryCompletePendingAsync(text, projectName, postHermesReplyAsync, appendSystemLogAsync)
                .ConfigureAwait(false);
        }

        if (!TradingManualOrderParser.TryParseOpenRequest(
                text,
                KnownSymbols(),
                out var draft,
                out var price)
            || draft is null)
        {
            if (TradingManualOrderParser.LooksLikeTradeIntent(text))
            {
                await postHermesReplyAsync(
                        projectName,
                        "Не удалось определить инструмент. Пример: «открой лонг по биткоину по рынку» или «открой шорт ETH на 100 USDT по рыночной».")
                    .ConfigureAwait(false);
                _log.LogWarn("[trade-open] trade intent but symbol not resolved");
                return true;
            }

            return false;
        }

        if (price.Kind == ManualPriceKind.Unspecified)
        {
            _pending = draft;
            var ask =
                $"По какой цене открыть {TradingManualOrderParser.FormatSideRu(draft.Side)} по {draft.Symbol}? "
                + $"Объём {FormatUsdtAmount(draft.QuantityUsdt ?? ResolveDefaultOrderUsdt())}. "
                + "Напишите «по рынку» / «по рыночной», цену лимита или «нет» для отмены.";
            await postHermesReplyAsync(projectName, ask).ConfigureAwait(false);
            _log.LogInfo(
                $"[trade-open] awaiting price symbol={draft.Symbol} side={draft.Side} qty_usdt={draft.QuantityUsdt ?? ResolveDefaultOrderUsdt()}");
            return true;
        }

        return await ExecuteOrderAsync(draft, price, projectName, postHermesReplyAsync, appendSystemLogAsync)
            .ConfigureAwait(false);
    }

    private async Task<bool> TryCompletePendingAsync(
        string text,
        string projectName,
        Func<string, string, Task> postHermesReplyAsync,
        Func<string, string, Task> appendSystemLogAsync)
    {
        var draft = _pending!;
        if (TradingManualOrderParser.IsCancel(text))
        {
            _pending = null;
            await postHermesReplyAsync(projectName, "Открытие позиции отменено.").ConfigureAwait(false);
            return true;
        }

        if (!TradingManualOrderParser.TryParsePriceOnly(text, out var price))
        {
            _pending = null;
            return false;
        }

        _pending = null;
        return await ExecuteOrderAsync(draft, price, projectName, postHermesReplyAsync, appendSystemLogAsync)
            .ConfigureAwait(false);
    }

    private async Task<bool> ExecuteOrderAsync(
        ManualOrderDraft draft,
        ManualPriceSpec price,
        string projectName,
        Func<string, string, Task> postHermesReplyAsync,
        Func<string, string, Task> appendSystemLogAsync)
    {
        if (price.Kind is ManualPriceKind.Limit or ManualPriceKind.Stop && price.Price <= 0)
        {
            _pending = draft;
            await postHermesReplyAsync(projectName, "Укажите цену для отложенного ордера (число).").ConfigureAwait(false);
            return true;
        }

        var useFutures = PreferFutures();
        var bridgeReady = useFutures
            ? await _futures.EnsureTerminalReadyAsync(force: true).ConfigureAwait(false)
            : await _spot.EnsureTerminalReadyAsync(force: true).ConfigureAwait(false);

        if (!bridgeReady)
        {
            await postHermesReplyAsync(
                    projectName,
                    useFutures
                        ? FuturesTerminalStatusReplyFormatter.TerminalUnavailableMessage()
                        : SpotTerminalStatusReplyFormatter.TerminalUnavailableMessage())
                .ConfigureAwait(false);
            return true;
        }

        var cmd = TradingManualOrderParser.BuildCommand(
            draft,
            price,
            ResolveDefaultOrderUsdt());
        _log.LogInfo(
            $"[trade-open] place_order {cmd.Symbol} {cmd.Side} {cmd.OrderType} qty_usdt={cmd.QuantityUsdt} price={cmd.Price} futures={useFutures}");

        await postHermesReplyAsync(projectName, TradingExecutionMessages.FormatCommandSent(cmd, useFutures))
            .ConfigureAwait(false);
        await appendSystemLogAsync(projectName, $"[trade-open] enqueue {cmd.OrderType} {cmd.Symbol} {cmd.Side}")
            .ConfigureAwait(false);

        var result = useFutures
            ? await FuturesTradingCommandExecutor.ExecuteAsync(_futures, cmd).ConfigureAwait(false)
            : await SpotTradingCommandExecutor.ExecuteAsync(_spot, cmd).ConfigureAwait(false);

        await postHermesReplyAsync(
                projectName,
                TradingExecutionMessages.FormatCommandResult(result.Ok, result.Detail, cmd, useFutures))
            .ConfigureAwait(false);
        await appendSystemLogAsync(projectName, $"[trade-open] ok={result.Ok} {result.Detail}").ConfigureAwait(false);
        return true;
    }

    private bool PreferFutures() => _futures.IsActiveForSession;

    private IReadOnlyList<string>? KnownSymbols()
    {
        var futures = _futures.TryReadFuturesSection();
        if (futures is not null)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(futures.SelectedSymbol))
            {
                list.Add(futures.SelectedSymbol);
            }

            list.AddRange(futures.Positions.Select(p => p.Symbol));
            list.AddRange(futures.OpenOrders.Select(o => o.Symbol));
            if (list.Count > 0)
            {
                return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        var spot = _spot.TryReadSpotSection();
        return spot?.Tickers.Select(t => t.Symbol).ToList();
    }

    private decimal ResolveDefaultOrderUsdt() =>
        RiskBasedQuantityCalculator.ResolveDefaultUsdt(_futures.TryReadFuturesSection());

    private static string FormatUsdtAmount(decimal usdt) =>
        $"{usdt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} USDT";
}
