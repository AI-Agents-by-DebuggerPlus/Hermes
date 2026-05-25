using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.Views.Dialogs;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class StrategiesViewModel : TradingPageViewModel
{
    private readonly TradingPlatformHost _host;

    public StrategiesViewModel(TradingReadModel readModel, TradingPlatformHost host)
        : base(readModel)
    {
        _host = host;

        ToggleStrategyCommand = new RelayCommand(p =>
        {
            if (p is StrategyCardItemViewModel card)
            {
                card.IsEnabled = !card.IsEnabled;
            }
        });

        ConfigureStrategyCommand = new RelayCommand(p =>
        {
            if (p is not StrategyCardItemViewModel card)
            {
                return;
            }

            var current = _host.GetStrategyParameters(card.Id);
            var dialog = new StrategyParametersDialog(card.Id, card.Name, current)
            {
                Owner = Application.Current?.MainWindow,
            };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
            {
                _host.UpdateStrategyParameters(dialog.Result);
            }
        });

        Refresh();
    }

    public ObservableCollection<StrategyCardItemViewModel> Strategies { get; } = [];
    public RelayCommand ToggleStrategyCommand { get; }
    public RelayCommand ConfigureStrategyCommand { get; }

    private void OnStrategyEnabledChangedByUser(StrategyCardItemViewModel card, bool enabled)
    {
        _host.SetStrategyEnabled(card.Id, enabled);
    }

    protected override void Refresh()
    {
        var dtos = ReadModel.GetStrategies();
        var dtoIds = dtos.Select(d => d.Id).ToHashSet();

        for (var i = Strategies.Count - 1; i >= 0; i--)
        {
            if (!dtoIds.Contains(Strategies[i].Id))
            {
                Strategies.RemoveAt(i);
            }
        }

        foreach (var s in dtos)
        {
            var card = Strategies.FirstOrDefault(x => x.Id == s.Id);
            if (card is null)
            {
                card = new StrategyCardItemViewModel
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    RiskProfile = s.RiskProfile,
                    Status = s.Status,
                };
                card.SyncingFromModel = true;
                card.IsEnabled = s.IsEnabled;
                card.SyncingFromModel = false;
                card.IsEnabledChangedByUser += OnStrategyEnabledChangedByUser;
                Strategies.Add(card);
                continue;
            }

            card.SyncingFromModel = true;
            if (card.Status != s.Status)
            {
                card.Status = s.Status;
            }

            if (card.IsEnabled != s.IsEnabled)
            {
                card.IsEnabled = s.IsEnabled;
            }

            card.SyncingFromModel = false;
        }
    }
}
