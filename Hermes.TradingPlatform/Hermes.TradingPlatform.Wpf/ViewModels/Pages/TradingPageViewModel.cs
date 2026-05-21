using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.Threading;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public abstract class TradingPageViewModel : BaseViewModel
{
    protected TradingPageViewModel(TradingReadModel readModel)
    {
        ReadModel = readModel;
        ReadModel.StateChanged += OnStateChanged;
    }

    protected TradingReadModel ReadModel { get; }

    protected virtual void OnStateChanged(object? sender, EventArgs e) =>
        WpfThreading.RunOnUi(Refresh);

    protected abstract void Refresh();
}
