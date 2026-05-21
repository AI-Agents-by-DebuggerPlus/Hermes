namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class StrategyCardItemViewModel : BaseViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string RiskProfile { get; init; }
    private string _status = "Idle";

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    private bool _isEnabled;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }
}
