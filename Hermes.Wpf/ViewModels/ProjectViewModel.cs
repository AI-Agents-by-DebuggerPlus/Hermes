using System.Collections.ObjectModel;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.ViewModels;

public sealed class ProjectViewModel : BaseViewModel
{
    private HermesProject? _selectedProject;
    private string _newProjectPath = string.Empty;

    public ObservableCollection<HermesProject> Projects { get; } = [];

    public HermesProject? SelectedProject
    {
        get => _selectedProject;
        set => SetProperty(ref _selectedProject, value);
    }

    public string NewProjectPath
    {
        get => _newProjectPath;
        set => SetProperty(ref _newProjectPath, value);
    }
}
