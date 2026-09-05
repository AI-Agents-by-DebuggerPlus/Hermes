using System.Windows;
using System.Windows.Input;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class ProjectManagerWindow : Window
{
    private readonly ProjectManagerViewModel _vm;
    private DateTime _lastClickUtc;
    private ProjectTileItem? _lastClickTile;

    public ProjectManagerWindow(MainViewModel main)
    {
        InitializeComponent();
        _vm = new ProjectManagerViewModel(main);
        DataContext = _vm;
        Title = "Agent Workspaces — плитки";
        Closed += (_, _) => _vm.Detach();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Tile_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ProjectTileItem tile)
        {
            return;
        }

        _vm.SelectTileCommand.Execute(tile);

        var now = DateTime.UtcNow;
        if (ReferenceEquals(_lastClickTile, tile) && (now - _lastClickUtc).TotalMilliseconds < 400)
        {
            if (_vm.OpenChatCommand.CanExecute(null))
            {
                _vm.OpenChatCommand.Execute(null);
            }
        }

        _lastClickTile = tile;
        _lastClickUtc = now;
    }

    private void Tile_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Capture focus for double-click detection on Up.
    }
}
