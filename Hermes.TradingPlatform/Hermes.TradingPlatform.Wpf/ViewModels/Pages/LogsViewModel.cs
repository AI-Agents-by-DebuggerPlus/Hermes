using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class LogsViewModel : BaseViewModel
{
    public LogsViewModel(MockTradingDataService data)
    {
        foreach (var log in data.GetLogs())
        {
            Entries.Add(log);
        }
    }

    public ObservableCollection<LogEntryDto> Entries { get; } = [];
}
