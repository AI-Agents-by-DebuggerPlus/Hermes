using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Core.Events;
using ExecutionMode = Hermes.SpotTerminal.Core.Enums.ExecutionMode;
using Hermes.SpotTerminal.Wpf.Bridge;
using Hermes.SpotTerminal.Wpf.Services;
using Hermes.SpotTerminal.Shared.Settings;

namespace Hermes.SpotTerminal.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SpotTerminalHost _host;
    private readonly SpotBridgePublisher _publisher;
    private readonly SpotBridgeCommandProcessor _commands;
    private string _logFilter = "All";

    public MainViewModel(SpotTerminalHost host, SpotBridgePublisher publisher, SpotBridgeCommandProcessor commands)
    {
        _host = host;
        _publisher = publisher;
        _commands = commands;
        _host.Start();
        _host.ReadModel.StateChanged += (_, _) => WpfUiDispatcher.Run(Refresh);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FeedStatus => _host.FeedStatusLabel;
    public string ExecutionModeLabel => _host.ExecutionMode.ToString();
    public string AgentThought => _host.StateStore.Snapshot.Agent.CurrentThought;

    public string LogFilter
    {
        get => _logFilter;
        set
        {
            _logFilter = value;
            RefreshLogs();
        }
    }

    public ObservableCollection<SpotBalance> Balances { get; } = [];
    public ObservableCollection<MarketTicker> Tickers { get; } = [];
    public ObservableCollection<SpotOrder> Orders { get; } = [];
    public ObservableCollection<PlatformLogEntry> Logs { get; } = [];
    public ObservableCollection<Skill> Skills { get; } = [];

    public void SetModeVirtual() => _host.SetExecutionMode(ExecutionMode.Virtual);
    public void SetModeSpotDemo() => _host.SetExecutionMode(ExecutionMode.SpotDemo);

    public void PlaceTestBuy() =>
        _ = PlaceTestMarketAsync(SpotOrderSide.Buy, "BTCUSDT", 0.001m);

    /// <summary>Market open for UI test buttons (Long = Buy, Short = Sell).</summary>
    public async Task<string> PlaceTestMarketAsync(SpotOrderSide side, string symbol, decimal quantity)
    {
        symbol = (symbol ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
        {
            return "Укажите символ (например BTCUSDT).";
        }

        if (quantity <= 0)
        {
            return "Объём должен быть больше 0.";
        }

        var price = Tickers.FirstOrDefault(t => t.Symbol == symbol)?.Price ?? 0m;
        var sideLabel = side == SpotOrderSide.Buy ? "Long (Buy)" : "Short (Sell)";

        try
        {
            var order = await _host.Gateway
                .PlaceOrderAsync(symbol, SpotOrderType.Market, side, quantity, price)
                .ConfigureAwait(false);

            var msg = $"{sideLabel} {symbol} qty={quantity:G29} → {order.Status} id={order.Id}";
            PublishTestLog(order.Status == SpotOrderStatus.Filled ? "OrderFilled" : "OrderTest", msg);

            if (order.Status == SpotOrderStatus.Filled)
            {
                _host.AgentMonitor.PublishTradeExecuted(msg, new { symbol, side = side.ToString(), quantity });
            }

            return msg;
        }
        catch (Exception ex)
        {
            var err = $"{sideLabel} {symbol}: {ex.Message}";
            PublishTestLog("OrderError", err);
            return err;
        }
    }

    public static bool TryParseQuantity(string? text, out decimal quantity)
    {
        quantity = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity)
               && quantity > 0;
    }

    private void PublishTestLog(string eventType, string message) =>
        _host.EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = eventType,
            Source = "TestUI",
            Message = message,
        }));

    public void AgentThoughtDemo() =>
        _host.AgentMonitor.PublishThought("Evaluating BTC momentum signal.", new { symbol = "BTCUSDT" }, "BTCUSDT");

    public void BacktestFirstSkill()
    {
        var skill = Skills.FirstOrDefault();
        if (skill is not null)
        {
            _host.SkillLifecycle.RunBacktest(skill.Id);
        }
    }

    public SpotPlatformSettingsDto LoadPlatformSettings() => _host.SettingsStore.Load();

    public void SaveBinanceApiKeys(string apiKey, string apiSecret) =>
        _host.UpdateBinanceCredentials(apiKey, apiSecret);

    private void Refresh()
    {
        Balances.Clear();
        foreach (var b in _host.ReadModel.GetBalances()) Balances.Add(b);
        Tickers.Clear();
        foreach (var t in _host.ReadModel.GetTickers()) Tickers.Add(t);
        Orders.Clear();
        foreach (var o in _host.ReadModel.GetOrders()) Orders.Add(o);
        Skills.Clear();
        foreach (var s in _host.ReadModel.GetSkills()) Skills.Add(s);
        RefreshLogs();
        Raise(nameof(FeedStatus), nameof(ExecutionModeLabel), nameof(AgentThought));
    }

    private void RefreshLogs()
    {
        Logs.Clear();
        var source = _logFilter == "Agent" ? "Agent" : null;
        foreach (var l in _host.ReadModel.GetLogs(source))
        {
            Logs.Add(l);
        }
    }

    private void Raise(params string[] names)
    {
        var handler = PropertyChanged;
        if (handler is null)
        {
            return;
        }

        foreach (var name in names)
        {
            handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public void Dispose()
    {
        _commands.Dispose();
        _publisher.Dispose();
        _host.Dispose();
    }
}
