using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

/// <summary>Local chat flow: open long/short → ask price → market/limit order via SpotTerminal bridge.</summary>
internal sealed class TradingManualOrderHandler
{
    private readonly SpotTerminalBridgeService _spot;
    private readonly LogService _log;
    private ManualOrderDraft? _pending;

    public TradingManualOrderHandler(SpotTerminalBridgeService spot, LogService log)
    {
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
        if (!tradingModeEnabled || !_spot.IsActiveForSession)
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
                        "Не удалось определить инструмент. Пример: «открой лонг BTCUSDT по рыночной» или «открой лонг по биткоину».")
                    .ConfigureAwait(false);
                _log.LogWarn("[spot-open] trade intent but symbol not resolved");
                return true;
            }

            return false;
        }

        if (price.Kind == ManualPriceKind.Unspecified)
        {
            _pending = draft;
            var ask =
                $"По какой цене открыть {TradingManualOrderParser.FormatSideRu(draft.Side)} по {draft.Symbol}? "
                + $"Объём {draft.Quantity}. Напишите «по рыночной», цену лимита или стопа; «нет» — отмена.";
            await postHermesReplyAsync(projectName, ask).ConfigureAwait(false);
            _log.LogInfo($"[spot-open] awaiting price symbol={draft.Symbol} side={draft.Side} qty={draft.Quantity}");
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

        if (!await _spot.EnsureTerminalReadyAsync(force: true).ConfigureAwait(false))
        {
            await postHermesReplyAsync(projectName, SpotTerminalStatusReplyFormatter.TerminalUnavailableMessage())
                .ConfigureAwait(false);
            return true;
        }

        var cmd = TradingManualOrderParser.BuildCommand(draft, price);
        _log.LogInfo(
            $"[spot-open] place_order {cmd.Symbol} {cmd.Side} {cmd.OrderType} qty={cmd.Quantity} price={cmd.Price}");
        await appendSystemLogAsync(projectName, $"[spot-open] enqueue {cmd.OrderType} {cmd.Symbol} {cmd.Side}")
            .ConfigureAwait(false);

        var result = await SpotTradingCommandExecutor.ExecuteAsync(_spot, cmd).ConfigureAwait(false);
        var line = TradingPlatformIntentParser.UserFacingLine(result.Ok, result.Detail);
        await postHermesReplyAsync(projectName, line).ConfigureAwait(false);
        await appendSystemLogAsync(projectName, $"[spot-open] ok={result.Ok} {result.Detail}").ConfigureAwait(false);
        return true;
    }

    private IReadOnlyList<string>? KnownSymbols()
    {
        var spot = _spot.TryReadSpotSection();
        return spot?.Tickers.Select(t => t.Symbol).ToList();
    }
}
