using System.Windows;
using System.Windows.Controls;
using Hermes.TradingPlatform.Wpf.Navigation;

namespace Hermes.TradingPlatform.Wpf.Controls;

public partial class SidebarButton : UserControl
{
    public static readonly DependencyProperty PageProperty =
        DependencyProperty.Register(nameof(Page), typeof(NavigationPage), typeof(SidebarButton));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SidebarButton));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(SidebarButton));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(SidebarButton));

    public SidebarButton() => InitializeComponent();

    public NavigationPage Page
    {
        get => (NavigationPage)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
}
