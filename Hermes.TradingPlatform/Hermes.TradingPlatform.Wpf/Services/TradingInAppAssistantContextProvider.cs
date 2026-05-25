using Hermes.InAppAssistant;
using Hermes.TradingPlatform.Wpf.Navigation;
using Hermes.TradingPlatform.Wpf.ViewModels.Shell;

namespace Hermes.TradingPlatform.Wpf.Services;

public sealed class TradingInAppAssistantContextProvider : IAppAssistantContextProvider
{
    private readonly Func<MainViewModel> _main;

    public TradingInAppAssistantContextProvider(Func<MainViewModel> main) => _main = main;

    public string GetLiveContextSnapshot()
    {
        var vm = _main();
        var page = vm.CurrentPage?.GetType().Name ?? "(none)";
        var title = vm.PageTitle;
        var conn = vm.ConnectionStatus;
        var account = vm.Account;
        var pnl = vm.Pnl;
        var positions = vm.OpenPositionsCount;
        var tradeLine = vm.TradeStatusLine;
        var settings = vm.Host.PlatformSettingsStore.Load();

        return $"""
            Application: Hermes Trading Platform
            Current page: {title} ({page})
            Connection: {conn}
            Account equity: {account.Equity:F2} · balance: {account.Balance:F2}
            PnL today: {pnl.Today:F2} · week: {pnl.Week:F2} · month: {pnl.Month:F2}
            Open positions: {positions}
            Trade UI status: {tradeLine}
            Market data source: {settings.MarketDataSource}
            Hermes orchestration: {(settings.HermesOrchestrationEnabled ? "enabled" : "disabled")}
            In-app assistant OpenRouter API key configured: {(!string.IsNullOrWhiteSpace(settings.InAppAssistantOpenRouterApiKey))}
            """;
    }
}
