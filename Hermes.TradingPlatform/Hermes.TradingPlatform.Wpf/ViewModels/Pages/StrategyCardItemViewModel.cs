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

    /// <summary>True while <see cref="Refresh"/> applies server state (do not push to host).</summary>
    internal bool SyncingFromModel { get; set; }

    public event Action<StrategyCardItemViewModel, bool>? IsEnabledChangedByUser;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetField(ref _isEnabled, value))
            {
                return;
            }

            if (!SyncingFromModel)
            {
                IsEnabledChangedByUser?.Invoke(this, value);
            }
        }
    }
}
