using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.ViewModels;

public sealed class GeneratedSkillsViewModel : BaseViewModel
{
    private readonly GeneratedSkillCatalogService _catalog;
    private readonly GeneratedSkillRunner _runner;
    private readonly Func<HermesSettings> _settings;
    private readonly LogService _log;
    private readonly Action<string>? _publishAssistantLine;
    private string _statusLine = string.Empty;

    public GeneratedSkillsViewModel(
        GeneratedSkillCatalogService catalog,
        GeneratedSkillRunner runner,
        Func<HermesSettings> settings,
        LogService log,
        Action<string>? publishAssistantLine = null)
    {
        _catalog = catalog;
        _runner = runner;
        _settings = settings;
        _log = log;
        _publishAssistantLine = publishAssistantLine;
        RefreshCommand = new RelayCommand(_ => _catalog.Reload());
        OpenFolderCommand = new RelayCommand(_ => OpenSkillsFolder());
        RunSkillCommand = new RelayCommand(
            async p => await RunSkillAsync(p as GeneratedSkillListItem).ConfigureAwait(true),
            p => p is GeneratedSkillListItem { Enabled: true });
        ToggleEnabledCommand = new RelayCommand(p => ToggleEnabled(p as GeneratedSkillListItem));
        _catalog.CatalogChanged += RepopulateItems;
        RepopulateItems();
    }

    public ObservableCollection<GeneratedSkillListItem> Items { get; } = [];

    public string StatusLine
    {
        get => _statusLine;
        private set => SetProperty(ref _statusLine, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand RunSkillCommand { get; }
    public ICommand ToggleEnabledCommand { get; }

    /// <summary>Updates the UI from the in-memory catalog (does not rescan disk).</summary>
    public void Refresh() => RepopulateItems();

    private void RepopulateItems()
    {
        Items.Clear();
        foreach (var skill in _catalog.Skills)
        {
            Items.Add(GeneratedSkillListItem.From(skill));
        }

        StatusLine = Items.Count == 0
            ? "Сгенерированных навыков пока нет."
            : $"{Items.Count} навык(ов) в {GeneratedSkillPaths.ResolveWindowsSkillsRoot(_settings())}";
    }

    private async Task RunSkillAsync(GeneratedSkillListItem? item)
    {
        if (item is null)
        {
            return;
        }

        var skill = _catalog.FindById(item.Id);
        if (skill is null)
        {
            StatusLine = $"Навык «{item.Id}» не найден.";
            return;
        }

        var run = await _runner.RunAsync(skill).ConfigureAwait(true);
        var line = SkillCrystallizeIntentParser.UserFacingRunLine(item.Id, run.Ok, run.Detail);
        StatusLine = line;
        _log.LogInfo($"[skill-ui] run {item.Id} ok={run.Ok}");
        _publishAssistantLine?.Invoke(line);
    }

    private void OpenSkillsFolder()
    {
        var path = GeneratedSkillPaths.ResolveWindowsSkillsRoot(_settings());
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private void ToggleEnabled(GeneratedSkillListItem? item)
    {
        if (item is null)
        {
            return;
        }

        var newEnabled = !item.Enabled;
        if (!_catalog.TrySetEnabled(item.Id, newEnabled))
        {
            StatusLine = $"Не удалось изменить «{item.Id}».";
            return;
        }

        StatusLine = newEnabled
            ? $"Навык «{item.Id}» включён."
            : $"Навык «{item.Id}» отключён.";
    }
}

public sealed class GeneratedSkillListItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Kind { get; init; }
    public required bool Enabled { get; init; }
    public required string TriggersDisplay { get; init; }

    public static GeneratedSkillListItem From(GeneratedSkillManifest skill) =>
        new()
        {
            Id = skill.Id,
            Title = string.IsNullOrWhiteSpace(skill.Title) ? skill.Id : skill.Title,
            Kind = skill.Kind,
            Enabled = skill.Enabled,
            TriggersDisplay = skill.Triggers.Count == 0
                ? "—"
                : string.Join(" • ", skill.Triggers.Take(3)),
        };
}
