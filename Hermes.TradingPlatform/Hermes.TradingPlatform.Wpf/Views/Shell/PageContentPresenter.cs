using System.Windows;
using System.Windows.Controls;
using Hermes.TradingPlatform.Wpf.Navigation;
using Hermes.TradingPlatform.Wpf.ViewModels;

namespace Hermes.TradingPlatform.Wpf.Views.Shell;

public sealed class PageContentPresenter : ContentControl
{
    public static readonly DependencyProperty PageViewModelProperty =
        DependencyProperty.Register(
            nameof(PageViewModel),
            typeof(BaseViewModel),
            typeof(PageContentPresenter),
            new PropertyMetadata(null, OnPageChanged));

    public BaseViewModel? PageViewModel
    {
        get => (BaseViewModel?)GetValue(PageViewModelProperty);
        set => SetValue(PageViewModelProperty, value);
    }

    private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PageContentPresenter presenter)
        {
            presenter.Content = ViewLocator.CreateContent(e.NewValue as BaseViewModel);
        }
    }
}
