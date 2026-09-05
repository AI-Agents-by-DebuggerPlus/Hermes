using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.ViewModels;

public sealed class ProjectTileItem : BaseViewModel
{
    private ImageSource? _avatar;
    private string? _ecosystemTitle;
    private Brush _ecosystemAccentBrush = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
    private bool _hasRelated;
    private bool _isSelected;
    private bool _hasAvatar;

    public ProjectTileItem(HermesProject project)
    {
        Project = project;
    }

    public HermesProject Project { get; }

    public string Name => Project.Name;

    public string Initial =>
        string.IsNullOrWhiteSpace(Project.Name)
            ? "?"
            : char.ToUpperInvariant(Project.Name.Trim()[0]).ToString();

    public string WindowsPath => Project.WindowsPath;

    public ImageSource? Avatar
    {
        get => _avatar;
        set
        {
            if (SetProperty(ref _avatar, value))
            {
                HasAvatar = value is not null;
            }
        }
    }

    public bool HasAvatar
    {
        get => _hasAvatar;
        private set => SetProperty(ref _hasAvatar, value);
    }

    public string? EcosystemTitle
    {
        get => _ecosystemTitle;
        set => SetProperty(ref _ecosystemTitle, value);
    }

    public Brush EcosystemAccentBrush
    {
        get => _ecosystemAccentBrush;
        set => SetProperty(ref _ecosystemAccentBrush, value);
    }

    public bool HasRelated
    {
        get => _hasRelated;
        set => SetProperty(ref _hasRelated, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ProjectManagerViewModel : BaseViewModel
{
    private readonly MainViewModel _main;
    private ProjectTileItem? _selectedTile;

    public ProjectManagerViewModel(MainViewModel main)
    {
        _main = main;
        Tiles = [];
        RebuildTiles();
        _main.Projects.Projects.CollectionChanged += Projects_OnCollectionChanged;
        _main.Projects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectViewModel.SelectedProject))
            {
                SyncSelection();
            }
        };

        SelectTileCommand = new RelayCommand(p =>
        {
            if (p is ProjectTileItem tile)
            {
                SelectedTile = tile;
                _main.Projects.SelectedProject = tile.Project;
            }
        });

        OpenChatCommand = new RelayCommand(
            _ => _main.RequestOpenChatWindow(),
            _ => _main.Projects.SelectedProject is not null);

        SetAvatarCommand = new RelayCommand(
            p =>
            {
                if (p is ProjectTileItem tile)
                {
                    _main.SetProjectAvatarFromDialog(tile.Project);
                    RefreshTile(tile);
                }
            },
            p => p is ProjectTileItem);

        ShowRelatedCommand = new RelayCommand(
            p =>
            {
                if (p is ProjectTileItem tile)
                {
                    _main.ShowProjectRelatedWindow(tile.Project);
                }
            },
            p => p is ProjectTileItem t && t.HasRelated);

        AddProjectCommand = _main.AddProjectCommand;
        BrowseProjectFolderCommand = _main.BrowseProjectFolderCommand;
        MoveProjectUpCommand = _main.MoveProjectUpCommand;
        MoveProjectDownCommand = _main.MoveProjectDownCommand;
    }

    public ObservableCollection<ProjectTileItem> Tiles { get; }

    public ProjectTileItem? SelectedTile
    {
        get => _selectedTile;
        set
        {
            if (!SetProperty(ref _selectedTile, value))
            {
                return;
            }

            foreach (var t in Tiles)
            {
                t.IsSelected = ReferenceEquals(t, value);
            }
        }
    }

    public ICommand SelectTileCommand { get; }
    public ICommand OpenChatCommand { get; }
    public ICommand SetAvatarCommand { get; }
    public ICommand ShowRelatedCommand { get; }
    public ICommand AddProjectCommand { get; }
    public ICommand BrowseProjectFolderCommand { get; }
    public ICommand MoveProjectUpCommand { get; }
    public ICommand MoveProjectDownCommand { get; }

    public string NewProjectPath
    {
        get => _main.Projects.NewProjectPath;
        set
        {
            _main.Projects.NewProjectPath = value;
            RaisePropertyChanged();
        }
    }

    public void Detach()
    {
        _main.Projects.Projects.CollectionChanged -= Projects_OnCollectionChanged;
    }

    private void Projects_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTiles();

    public void RebuildTiles()
    {
        Tiles.Clear();
        foreach (var project in _main.Projects.Projects)
        {
            var tile = new ProjectTileItem(project);
            RefreshTile(tile);
            Tiles.Add(tile);
        }

        SyncSelection();
    }

    private void RefreshTile(ProjectTileItem tile)
    {
        var meta = _main.GetProjectUiMeta(tile.WindowsPath);
        tile.Avatar = LoadAvatar(meta?.AvatarPath);
        var eco = ProjectEcosystemCatalog.Resolve(tile.Project, meta);
        tile.EcosystemTitle = eco?.Title;
        tile.EcosystemAccentBrush = ParseAccent(eco?.AccentHex);

        var siblings = _main.Projects.Projects
            .Where(p => !string.Equals(p.WindowsPath, tile.WindowsPath, StringComparison.OrdinalIgnoreCase))
            .Count(p =>
            {
                var m = _main.GetProjectUiMeta(p.WindowsPath);
                var e = ProjectEcosystemCatalog.Resolve(p, m);
                return e is not null && eco is not null && e.Id == eco.Id;
            });

        tile.HasRelated = eco is not null && (eco.Apps.Count > 0 || siblings > 0);
    }

    private void SyncSelection()
    {
        var selected = _main.Projects.SelectedProject;
        SelectedTile = selected is null
            ? null
            : Tiles.FirstOrDefault(t =>
                string.Equals(t.WindowsPath, selected.WindowsPath, StringComparison.OrdinalIgnoreCase));
    }

    private static ImageSource? LoadAvatar(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            bmp.DecodePixelWidth = 128;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static Brush ParseAccent(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // fall through
        }

        var fallback = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
        fallback.Freeze();
        return fallback;
    }
}
