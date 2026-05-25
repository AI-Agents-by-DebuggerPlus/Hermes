using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class LogsViewModel : TradingPageViewModel
{
    private readonly TradingPlatformHost _host;

    public LogsViewModel(TradingReadModel readModel, TradingPlatformHost host)
        : base(readModel)
    {
        _host = host;
        ClearLogsCommand = new RelayCommand(_ => ClearLogs());
        Refresh();
    }

    public ObservableCollection<LogEntryDto> Entries { get; } = [];

    public RelayCommand ClearLogsCommand { get; }

    private void ClearLogs()
    {
        _host.ClearPlatformLogs();
        Refresh();
    }

    protected override void Refresh()
    {
        Entries.Clear();
        foreach (var log in ReadModel.GetLogs())
        {
            Entries.Add(log);
        }
    }
}
