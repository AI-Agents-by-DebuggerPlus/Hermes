using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

/// <summary>Local chat flow: open long/short → ask price → market/limit/stop order via bridge (no strategy).</summary>
internal sealed class TradingManualOrderHandler
{
    private readonly TradingPlatformBridgeService _bridge;
    private readonly LogService _log;
    private ManualOrderDraft? _pending;

    public TradingManualOrderHandler(TradingPlatformBridgeService bridge, LogService log)
    {
        _bridge = bridge;
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
        if (!tradingModeEnabled || !_bridge.IsIntegrationEnabled)
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
            return false;
        }

        if (price.Kind == ManualPriceKind.Unspecified)
        {
            _pending = draft;
            var ask =
                $"По какой цене открыть {TradingManualOrderParser.FormatSideRu(draft.Side)} по {draft.Symbol}? "
                + $"Объём {draft.Quantity} (paper). Напишите «по рыночной», цену лимита или стопа; «нет» — отмена.";
            await postHermesReplyAsync(projectName, ask).ConfigureAwait(false);
            _log.LogInfo($"[trading-open] awaiting price symbol={draft.Symbol} side={draft.Side} qty={draft.Quantity}");
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

        _bridge.EnsureTerminalRunning(force: true);
        if (!_bridge.IsTerminalAlive())
        {
            await postHermesReplyAsync(projectName, TradingStatusReplyFormatter.TerminalUnavailableMessage())
                .ConfigureAwait(false);
            return true;
        }

        var cmd = TradingManualOrderParser.BuildCommand(draft, price);
        _log.LogInfo(
            $"[trading-open] place_order {cmd.Symbol} {cmd.Side} {cmd.OrderType} qty={cmd.Quantity} price={cmd.Price}");
        await appendSystemLogAsync(projectName, $"[trading-open] enqueue {cmd.OrderType} {cmd.Symbol} {cmd.Side}")
            .ConfigureAwait(false);

        var result = await _bridge.TryEnqueueCommandAsync(cmd).ConfigureAwait(false);
        var line = TradingPlatformIntentParser.UserFacingLine(result.Ok, result.Detail);
        await postHermesReplyAsync(projectName, line).ConfigureAwait(false);
        await appendSystemLogAsync(projectName, $"[trading-open] ok={result.Ok} {result.Detail}").ConfigureAwait(false);
        return true;
    }

    private IReadOnlyList<string>? KnownSymbols()
    {
        var snap = _bridge.TryReadSnapshot();
        return snap?.Tickers.Select(t => t.Symbol).ToList();
    }
}
