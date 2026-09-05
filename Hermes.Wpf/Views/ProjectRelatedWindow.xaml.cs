using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public sealed class ProjectRelatedItem
{
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string ActionLabel { get; init; }
    public HermesProject? Project { get; init; }
    public ProjectRelatedAppInfo? App { get; init; }
}

public sealed class ProjectRelatedViewModel : BaseViewModel
{
    private readonly MainViewModel _main;

    public ProjectRelatedViewModel(MainViewModel main, HermesProject source)
    {
        _main = main;
        var meta = main.GetProjectUiMeta(source.WindowsPath);
        var eco = ProjectEcosystemCatalog.Resolve(source, meta);
        Title = eco is null ? $"Связанное — {source.Name}" : $"{eco.Title} — {source.Name}";
        Subtitle = eco is null
            ? "Экосистема не определена. Можно задать вручную позже или переименовать папку (trading / english / …)."
            : $"Проекты и приложения группы «{eco.Title}».";

        Items = [];
        if (eco is not null)
        {
            foreach (var p in main.Projects.Projects)
            {
                if (string.Equals(p.WindowsPath, source.WindowsPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var m = main.GetProjectUiMeta(p.WindowsPath);
                var e = ProjectEcosystemCatalog.Resolve(p, m);
                if (e?.Id != eco.Id)
                {
                    continue;
                }

                Items.Add(new ProjectRelatedItem
                {
                    Kind = "project",
                    Title = p.Name,
                    Detail = p.WindowsPath,
                    ActionLabel = "Выбрать",
                    Project = p,
                });
            }

            foreach (var app in eco.Apps)
            {
                Items.Add(new ProjectRelatedItem
                {
                    Kind = "app",
                    Title = app.Title,
                    Detail = app.Description,
                    ActionLabel = "Запуск",
                    App = app,
                });
            }
        }

        ActCommand = new RelayCommand(p =>
        {
            if (p is not ProjectRelatedItem item)
            {
                return;
            }

            if (item.Project is not null)
            {
                _main.Projects.SelectedProject = item.Project;
                return;
            }

            if (item.App is not null)
            {
                _main.LaunchRelatedApp(item.App);
            }
        });
    }

    public string Title { get; }
    public string Subtitle { get; }
    public ObservableCollection<ProjectRelatedItem> Items { get; }
    public ICommand ActCommand { get; }
}

public partial class ProjectRelatedWindow : Window
{
    public ProjectRelatedWindow(MainViewModel main, HermesProject source)
    {
        InitializeComponent();
        DataContext = new ProjectRelatedViewModel(main, source);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
