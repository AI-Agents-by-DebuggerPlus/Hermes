using Hermes.TradingPlatform.Wpf.Navigation;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Shell;

public sealed class NavItemViewModel : BaseViewModel
{
    public NavItemViewModel(NavigationPage page, string title, string iconGlyph, string? toolTipRu = null)
    {
        Page = page;
        Title = title;
        IconGlyph = iconGlyph;
        ToolTipRu = toolTipRu ?? title;
    }

    public NavigationPage Page { get; }
    public string Title { get; }
    public string IconGlyph { get; }
    public string ToolTipRu { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
