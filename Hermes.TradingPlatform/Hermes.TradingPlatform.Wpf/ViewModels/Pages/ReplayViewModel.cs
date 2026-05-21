using Hermes.TradingPlatform.Wpf.Commands;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class ReplayViewModel : BaseViewModel
{
    public ReplayViewModel()
    {
        PlayCommand = new RelayCommand(_ => IsPlaying = true);
        PauseCommand = new RelayCommand(_ => IsPlaying = false);
        Speed1xCommand = new RelayCommand(_ => Speed = "1x");
        Speed2xCommand = new RelayCommand(_ => Speed = "2x");
        Speed4xCommand = new RelayCommand(_ => Speed = "4x");
    }

    private bool _isPlaying;
    private string _speed = "1x";
    private DateTime _currentTime = DateTime.Today.AddHours(9);
    private DateTime _sessionStart = DateTime.Today.AddHours(9);
    private DateTime _sessionEnd = DateTime.Today.AddHours(17);

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    public string Speed
    {
        get => _speed;
        set => SetField(ref _speed, value);
    }

    public DateTime CurrentTime
    {
        get => _currentTime;
        set => SetField(ref _currentTime, value);
    }

    public DateTime SessionStart => _sessionStart;
    public DateTime SessionEnd => _sessionEnd;

    public string StatusLine => $"Replay @ {CurrentTime:HH:mm:ss} — {Speed} — {(IsPlaying ? "Playing" : "Paused")}";

    public RelayCommand PlayCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand Speed1xCommand { get; }
    public RelayCommand Speed2xCommand { get; }
    public RelayCommand Speed4xCommand { get; }
}
