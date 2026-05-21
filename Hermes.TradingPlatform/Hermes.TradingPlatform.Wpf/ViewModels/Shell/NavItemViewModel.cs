using Hermes.TradingPlatform.Wpf.Navigation;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Shell;

public sealed class NavItemViewModel : BaseViewModel
{
    public NavItemViewModel(NavigationPage page, string title, string iconGlyph)
    {
        Page = page;
        Title = title;
        IconGlyph = iconGlyph;
    }

    public NavigationPage Page { get; }
    public string Title { get; }
    public string IconGlyph { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
