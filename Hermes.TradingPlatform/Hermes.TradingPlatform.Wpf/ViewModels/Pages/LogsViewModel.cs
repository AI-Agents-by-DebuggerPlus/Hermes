using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class LogsViewModel : TradingPageViewModel
{
    public LogsViewModel(TradingReadModel readModel)
        : base(readModel) => Refresh();

    public ObservableCollection<LogEntryDto> Entries { get; } = [];

    protected override void Refresh()
    {
        Entries.Clear();
        foreach (var log in ReadModel.GetLogs())
        {
            Entries.Add(log);
        }
    }
}
