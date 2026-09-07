using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Hermes.StrategyViewer.Models;
using Hermes.StrategyViewer.Services;

namespace Hermes.StrategyViewer.ViewModels;

public sealed class ConditionRowViewModel : INotifyPropertyChanged
{
    private ConditionStatus _status;

    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";

    public ConditionStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusGlyph));
            OnPropertyChanged(nameof(ForegroundBrush));
        }
    }

    public string StatusGlyph => Status switch
    {
        ConditionStatus.Confirmed => "●",
        ConditionStatus.Rejected => "●",
        _ => "○",
    };

    public Brush ForegroundBrush => Status switch
    {
        ConditionStatus.Confirmed => Brushes.LimeGreen,
        ConditionStatus.Rejected => Brushes.IndianRed,
        _ => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ScenarioCardViewModel : INotifyPropertyChanged
{
    private ConditionStatus _aggregateStatus = ConditionStatus.Pending;

    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public Brush AccentBrush { get; init; } = Brushes.White;
    public string TakeProfitText { get; init; } = "";
    public string StopLossText { get; init; } = "";
    public List<ConditionRowViewModel> Conditions { get; } = [];

    public ConditionStatus AggregateStatus
    {
        get => _aggregateStatus;
        set
        {
            if (_aggregateStatus == value)
            {
                return;
            }

            _aggregateStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public Brush BorderBrush => AggregateStatus switch
    {
        ConditionStatus.Confirmed => AccentBrush,
        ConditionStatus.Rejected => Brushes.IndianRed,
        _ => new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly StrategyLogService _log;
    private readonly BinanceMarketDataService _market;
    private readonly System.Timers.Timer _pollTimer;
    private StrategyDocument? _document;
    private string? _strategyPath;
    private string _title = "STRATEGY";
    private string _headerSub = "";
    private string _livePriceText = "—";
    private string _changeText = "";
    private string _sourceText = "";
    private string _statusText = "Ready";
    private string _disclaimer = "";
    private string _trendText = "";
    private string _warningText = "";
    private string _rsiText = "";
    private string _stochText = "";
    private string _volatilityLabel = "";
    private double _volatilityFill = 0.7;
    private List<string> _riskNotes = [];

    public MainViewModel(StrategyLogService log)
    {
        _log = log;
        _market = new BinanceMarketDataService(log);
        Scenarios = [];
        _pollTimer = new System.Timers.Timer(8000) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) =>
        {
            _ = Application.Current.Dispatcher.InvokeAsync(RefreshMarketAsync);
        };
    }

    public IReadOnlyList<ScenarioCardViewModel> Scenarios { get; private set; }

    public IReadOnlyList<string> RiskNotes => _riskNotes;

    public string Title
    {
        get => _title;
        private set { _title = value; OnPropertyChanged(); }
    }

    public string HeaderSub
    {
        get => _headerSub;
        private set { _headerSub = value; OnPropertyChanged(); }
    }

    public string LivePriceText
    {
        get => _livePriceText;
        private set { _livePriceText = value; OnPropertyChanged(); }
    }

    public string ChangeText
    {
        get => _changeText;
        private set { _changeText = value; OnPropertyChanged(); }
    }

    public string SourceText
    {
        get => _sourceText;
        private set { _sourceText = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string Disclaimer
    {
        get => _disclaimer;
        private set { _disclaimer = value; OnPropertyChanged(); }
    }

    public string TrendText
    {
        get => _trendText;
        private set { _trendText = value; OnPropertyChanged(); }
    }

    public string WarningText
    {
        get => _warningText;
        private set { _warningText = value; OnPropertyChanged(); }
    }

    public string RsiText
    {
        get => _rsiText;
        private set { _rsiText = value; OnPropertyChanged(); }
    }

    public string StochText
    {
        get => _stochText;
        private set { _stochText = value; OnPropertyChanged(); }
    }

    public string VolatilityLabel
    {
        get => _volatilityLabel;
        private set { _volatilityLabel = value; OnPropertyChanged(); }
    }

    public double VolatilityFill
    {
        get => _volatilityFill;
        private set { _volatilityFill = value; OnPropertyChanged(); }
    }

    public StrategyLogService Log => _log;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void LoadStrategy(string path)
    {
        _strategyPath = path;
        _document = StrategyLoader.Load(path);
        _log.Info($"Loaded strategy: {path}");
        ApplyDocumentStatic(_document);
        _ = RefreshMarketAsync();
        _pollTimer.Start();
    }

    public async Task RefreshMarketAsync()
    {
        if (_document is null)
        {
            return;
        }

        StatusText = "Updating market…";
        var snap = await _market.FetchGoldSnapshotAsync().ConfigureAwait(true);
        if (snap is null)
        {
            StatusText = "Market update failed";
            return;
        }

        if (!string.IsNullOrWhiteSpace(snap.FetchError))
        {
            StatusText = $"Market error: {snap.FetchError}";
            SourceText = snap.SourceLabel;
            return;
        }

        LivePriceText = $"${snap.LastPrice:N2}";
        var sign = snap.ChangeAbs >= 0 ? "+" : "";
        ChangeText = $"{sign}{snap.ChangeAbs:N2} ({sign}{snap.ChangePct:N2}%)";
        SourceText = $"{snap.SourceLabel} · updated {snap.UpdatedAt:HH:mm:ss}";
        HeaderSub = $"Live · {snap.ApiSymbol} · strategy as_of {_document.AsOf?[..10] ?? "—"}";

        RsiText = snap.Rsi14Daily is null ? "RSI: —" : $"RSI: {snap.Rsi14Daily:F1} ({ClassifyRsi(snap.Rsi14Daily.Value)})";
        StochText = snap.StochKDaily is null ? "Stoch: —" : $"Stoch: {snap.StochKDaily:F1} ({ClassifyStoch(snap.StochKDaily.Value)})";
        TrendText = snap.LastPrice > (snap.Ema10Daily ?? snap.LastPrice) ? "Strong Bullish" : "Neutral / Mixed";
        WarningText = snap.Rsi14Daily >= 70 ? "ВНИМАНИЕ: Зона перекупленности!" : "";

        UpdateScenariosLive(snap);
        StatusText = "Live";
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        _market.Dispose();
    }

    private void ApplyDocumentStatic(StrategyDocument doc)
    {
        Title = doc.Title;
        Disclaimer = doc.Disclaimer ?? "";
        VolatilityLabel = doc.Risk?.VolatilityLabel ?? "";
        VolatilityFill = doc.Risk?.VolatilityLabel?.Contains("Выс", StringComparison.OrdinalIgnoreCase) == true ? 0.85 : 0.55;
        _riskNotes = doc.Risk?.Notes?.ToList() ?? [];
        OnPropertyChanged(nameof(RiskNotes));

        if (doc.PriceSnapshot?.Last is not null && LivePriceText == "—")
        {
            LivePriceText = $"${doc.PriceSnapshot.Last:N2}";
            ChangeText = doc.PriceSnapshot.ChangeAbs is null
                ? ""
                : $"{(doc.PriceSnapshot.ChangeAbs >= 0 ? "+" : "")}{doc.PriceSnapshot.ChangeAbs:N2} ({doc.PriceSnapshot.ChangePct:N2}%)";
        }

        HeaderSub = doc.AsOf is not null
            ? $"Snapshot · {doc.Symbol} · as_of {doc.AsOf[..10]}"
            : doc.Symbol;

        TrendText = doc.Technical?.Trend ?? "";
        WarningText = doc.Technical?.Warning ?? "";
        var rsi = doc.Technical?.Indicators.FirstOrDefault(i => i.Id == "rsi_14");
        var stoch = doc.Technical?.Indicators.FirstOrDefault(i => i.Id == "stoch_k");
        if (rsi?.Value is not null)
        {
            RsiText = $"RSI: {rsi.Value:F1} ({rsi.State ?? ""})";
        }

        if (stoch?.Value is not null)
        {
            StochText = $"Stoch: {stoch.Value:F1} ({stoch.State ?? ""})";
        }

        Scenarios = doc.Scenarios.Select(MapScenarioStatic).ToList();
        OnPropertyChanged(nameof(Scenarios));
    }

    private void UpdateScenariosLive(MarketSnapshot snap)
    {
        if (_document is null)
        {
            return;
        }

        foreach (var card in Scenarios)
        {
            var scenario = _document.Scenarios.FirstOrDefault(s => s.Id == card.Id);
            if (scenario is null)
            {
                continue;
            }

            var evals = ScenarioEvaluator.Evaluate(scenario, snap);
            for (var i = 0; i < card.Conditions.Count && i < evals.Count; i++)
            {
                card.Conditions[i].Status = evals[i].Status;
            }

            card.AggregateStatus = ScenarioEvaluator.AggregateStatus(evals);
        }
    }

    private static ScenarioCardViewModel MapScenarioStatic(StrategyScenario s)
    {
        var tp = s.TakeProfit is { Count: > 0 }
            ? string.Join(" / ", s.TakeProfit.Select(v => $"${v:N0}"))
            : "—";
        var card = new ScenarioCardViewModel
        {
            Id = s.Id,
            Title = s.Title,
            Subtitle = s.Subtitle ?? "",
            AccentBrush = AccentBrush(s.Accent),
            TakeProfitText = tp,
            StopLossText = s.StopLoss is null ? "—" : $"${s.StopLoss:N0}",
            AggregateStatus = ConditionStatus.Pending,
        };

        foreach (var c in s.Conditions)
        {
            card.Conditions.Add(new ConditionRowViewModel
            {
                Label = c.Label,
                Detail = "",
                Status = ConditionStatus.Pending,
            });
        }

        return card;
    }

    private static Brush AccentBrush(string? accent) => accent switch
    {
        "emerald" => new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
        "cyan" => new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)),
        "rose" => new SolidColorBrush(Color.FromRgb(0xFB, 0x71, 0x85)),
        "gold" => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
        _ => new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
    };

    private static string ClassifyRsi(double v) =>
        v >= 70 ? "Overbought" : v <= 30 ? "Oversold" : "Neutral";

    private static string ClassifyStoch(double v) =>
        v >= 80 ? "Extreme" : v <= 20 ? "Oversold" : "Neutral";

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
