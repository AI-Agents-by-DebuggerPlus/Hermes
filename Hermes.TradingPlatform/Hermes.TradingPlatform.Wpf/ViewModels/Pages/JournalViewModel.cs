using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class JournalViewModel : TradingPageViewModel
{
    public JournalViewModel(TradingReadModel readModel)
        : base(readModel) => Refresh();

    public ObservableCollection<TradeJournalEntryDto> Entries { get; } = [];

    protected override void Refresh()
    {
        Entries.Clear();
        foreach (var entry in ReadModel.GetJournal())
        {
            Entries.Add(entry);
        }
    }
}
