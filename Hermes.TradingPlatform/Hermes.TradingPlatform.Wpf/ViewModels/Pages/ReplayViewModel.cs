using System.Collections.ObjectModel;
using System.Globalization;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services.Replay;
using Hermes.TradingPlatform.Wpf.Threading;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class ReplayViewModel : BaseViewModel
{
    private readonly JournalReplayService _replay;
    private readonly string? _journalFilePath;

    public ReplayViewModel(JournalReplayService replay, string? journalFilePath = null)
    {
        _replay = replay;
        _journalFilePath = journalFilePath;
        _replay.StateChanged += OnReplayChanged;

        PlayCommand = new RelayCommand(_ => _replay.Play(), _ => Entries.Count > 0);
        PauseCommand = new RelayCommand(_ => _replay.Pause(), _ => _replay.IsPlaying);
        Speed1xCommand = new RelayCommand(_ => _replay.SetSpeed(1d));
        Speed2xCommand = new RelayCommand(_ => _replay.SetSpeed(2d));
        Speed4xCommand = new RelayCommand(_ => _replay.SetSpeed(4d));
        StepForwardCommand = new RelayCommand(_ => _replay.StepForward(), _ => Entries.Count > 0);
        StepBackCommand = new RelayCommand(_ => _replay.StepBack(), _ => Entries.Count > 0);
        ReloadCommand = new RelayCommand(_ => Reload());
        JumpToCommand = new RelayCommand(p =>
        {
            if (p is double d)
            {
                _replay.JumpTo((int)Math.Round(d));
            }
            else if (p is int i)
            {
                _replay.JumpTo(i);
            }
        });

        Reload();
    }

    public ObservableCollection<TradeJournalEntry> Entries { get; } = [];

    public int CurrentIndex
    {
        get => _replay.CurrentIndex;
        set
        {
            if (value != _replay.CurrentIndex)
            {
                _replay.JumpTo(value);
            }
        }
    }

    public bool IsPlaying => _replay.IsPlaying;

    public string Speed => _replay.Speed switch
    {
        1d => "1x",
        2d => "2x",
        4d => "4x",
        _ => $"{_replay.Speed:0.##}x",
    };

    public TradeJournalEntry? CurrentEntry => _replay.CurrentEntry;

    public string StatusLine =>
        $"Replay {(_replay.IsPlaying ? "▶" : "⏸")}  {Speed}  ·  {_replay.FormatProgress()}";

    public string CurrentSummary
    {
        get
        {
            var e = _replay.CurrentEntry;
            if (e is null)
            {
                return "No entry selected.";
            }

            var sign = e.RealizedPnl >= 0 ? "+" : string.Empty;
            return $"{e.Symbol}  {e.Side} {e.Kind}  qty={e.Quantity:0.####}  @ {e.FillPrice:N2}  "
                + $"PnL: {sign}{e.RealizedPnl:N2}  bal: {e.BalanceBefore:N2} → {e.BalanceAfter:N2}";
        }
    }

    public int MaxIndex => Math.Max(0, Entries.Count - 1);

    public RelayCommand PlayCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand Speed1xCommand { get; }
    public RelayCommand Speed2xCommand { get; }
    public RelayCommand Speed4xCommand { get; }
    public RelayCommand StepForwardCommand { get; }
    public RelayCommand StepBackCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand JumpToCommand { get; }

    private void Reload()
    {
        _replay.Reload(_journalFilePath);
        WpfThreading.RunOnUi(() =>
        {
            Entries.Clear();
            foreach (var entry in _replay.Entries)
            {
                Entries.Add(entry);
            }

            RaiseAllReplayProperties();
        });
    }

    private void OnReplayChanged(object? sender, EventArgs e) =>
        WpfThreading.RunOnUi(RaiseAllReplayProperties);

    private void RaiseAllReplayProperties()
    {
        Raise(nameof(CurrentIndex));
        Raise(nameof(IsPlaying));
        Raise(nameof(Speed));
        Raise(nameof(CurrentEntry));
        Raise(nameof(CurrentSummary));
        Raise(nameof(StatusLine));
        Raise(nameof(MaxIndex));
        PlayCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        StepForwardCommand.RaiseCanExecuteChanged();
        StepBackCommand.RaiseCanExecuteChanged();
    }
}
