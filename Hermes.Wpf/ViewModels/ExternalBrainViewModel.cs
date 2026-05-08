using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.ViewModels;

public sealed class ExternalBrainViewModel : BaseViewModel
{
    private readonly ExternalBrainService _brain;
    private readonly LogService _log;

    private readonly ObservableCollection<MemoryItem> _backing = new();

    private MemoryItem? _selectedMemory;
    private FlowDocument? _previewFlow;
    private string _searchText = string.Empty;
    private string _tagFilter = string.Empty;
    private string _statusLine = "Загрузка…";
    private bool _refreshBusy;

    private readonly DispatcherTimer _searchDebounce;

    public ExternalBrainViewModel(ExternalBrainService brain, LogService log)
    {
        _brain = brain;
        _log = log;

        MemoriesView = CollectionViewSource.GetDefaultView(_backing);
        MemoriesView.SortDescriptions.Clear();
        MemoriesView.SortDescriptions.Add(new SortDescription(nameof(MemoryItem.Timestamp), ListSortDirection.Descending));
        MemoriesView.GroupDescriptions.Clear();
        MemoriesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MemoryItem.DateGroupKey)));

        _selectedTimeChoice = TimeChoices.Count > 0 ? TimeChoices[0] : new NamedTimePreset(TimeFilterPreset.All, "Все");

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _ = ApplyFiltersFromSearchAsync();
        };

        RefreshCommand = new RelayCommand(async _ => await RefreshFromDiskAsync(), _ => !_refreshBusy);
        FilterCommand = new RelayCommand(_ => _ = ApplyFiltersFromSearchAsync());
        SearchCommand = new RelayCommand(
            async _ =>
            {
                _searchDebounce.Stop();
                await ApplyFiltersFromSearchAsync();
            },
            _ => !_refreshBusy);
        OpenVaultFolderCommand = new RelayCommand(
            _ => OpenVaultFolder(),
            _ => Directory.Exists((_brain.ResolveEffectiveMemoryPath() ?? string.Empty).Trim()));

        brain.MemoriesChanged += OnBrainMemoriesChanged;

        _ = InitialLoadAsync();
    }

    private void OnBrainMemoriesChanged()
    {
        _ = ApplyFiltersFromSearchAsync();
    }

    public ICollectionView MemoriesView { get; }

    public ICommand RefreshCommand { get; }
    public ICommand FilterCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand OpenVaultFolderCommand { get; }

    public ObservableCollection<NamedTimePreset> TimeChoices { get; } =
        new(
        [
            new NamedTimePreset(TimeFilterPreset.All, "Все"),
            new NamedTimePreset(TimeFilterPreset.Today, "Сегодня"),
            new NamedTimePreset(TimeFilterPreset.Week, "7 дней"),
        ]);

    private NamedTimePreset _selectedTimeChoice;

    public NamedTimePreset SelectedTimeChoice
    {
        get => _selectedTimeChoice;
        set
        {
            if (!SetProperty(ref _selectedTimeChoice, value))
            {
                return;
            }

            _ = ApplyFiltersFromSearchAsync();
        }
    }

    public void DetachFromBrainEvents()
    {
        _brain.MemoriesChanged -= OnBrainMemoriesChanged;
    }

    public string ResolvedVaultPath =>
        string.IsNullOrWhiteSpace(_brain.ResolveEffectiveMemoryPath())
            ? "(путь не задан)"
            : _brain.ResolveEffectiveMemoryPath();

    public string StatusLine
    {
        get => _statusLine;
        private set => SetProperty(ref _statusLine, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            _searchDebounce.Stop();
            _searchDebounce.Start();
        }
    }

    public string TagFilter
    {
        get => _tagFilter;
        set
        {
            if (SetProperty(ref _tagFilter, value ?? string.Empty))
            {
                _ = ApplyFiltersFromSearchAsync();
            }
        }
    }

    public MemoryItem? SelectedMemory
    {
        get => _selectedMemory;
        set
        {
            if (!SetProperty(ref _selectedMemory, value))
            {
                return;
            }

            RebuildPreviewFlow();
        }
    }

    public FlowDocument? PreviewFlow
    {
        get => _previewFlow;
        private set => SetProperty(ref _previewFlow, value);
    }

    private async Task InitialLoadAsync()
    {
        try
        {
            await ApplyFiltersFromSearchAsync();
        }
        catch (Exception ex)
        {
            _log.LogError($"[external-brain-ui] {ex.Message}");
            StatusLine = $"Ошибка: {ex.Message}";
        }
    }

    private async Task RefreshFromDiskAsync()
    {
        _refreshBusy = true;
        CommandManager.InvalidateRequerySuggested();
        StatusLine = "Перезагрузка с диска…";
        try
        {
            _brain.RestartWatcherAndReload("ui-refresh");
            await Task.Delay(50).ConfigureAwait(true);
            await ApplyFiltersFromSearchAsync();
        }
        finally
        {
            _refreshBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ApplyFiltersFromSearchAsync()
    {
        try
        {
            var path = _brain.ResolveEffectiveMemoryPath();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                _backing.Clear();
                StatusLine =
                    "USER ACTION REQUIRED: Задайте папку памяти Obsidian (External Brain) в Settings или %AppData%\\HermesWpf\\externalBrain.json { \"MemoryPath\":\"...\" } или переменную HERMES_EXTERNAL_BRAIN_PATH.";
                RaisePropertyChanged(nameof(ResolvedVaultPath));
                RebuildPreviewFlow();
                return;
            }

            var list = await _brain.SearchAsync(SearchText).ConfigureAwait(true);
            var tag = TagFilter.Trim().TrimStart('#').ToLowerInvariant();
            if (!string.IsNullOrEmpty(tag))
            {
                list = list
                    .Where(m => m.Tags.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var now = DateTime.Now;
            list = SelectedTimeChoice.Value switch
            {
                TimeFilterPreset.Today => list.Where(m => m.Timestamp.Date == now.Date).ToList(),
                TimeFilterPreset.Week => list.Where(m => m.Timestamp >= now.AddDays(-7)).ToList(),
                _ => list,
            };

            _backing.Clear();
            foreach (var m in list.OrderByDescending(static x => x.Timestamp))
            {
                _backing.Add(m);
            }

            MemoriesView.Refresh();

            StatusLine = $"Показано: {_backing.Count} записей. Vault: {path}";
            RaisePropertyChanged(nameof(ResolvedVaultPath));

            if (SelectedMemory is not null
                && !_backing.Any(x =>
                    string.Equals(x.SourceFile, SelectedMemory.SourceFile, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedMemory = null;
            }

            RebuildPreviewFlow();
        }
        catch (Exception ex)
        {
            _log.LogError($"[external-brain-ui] filter: {ex.Message}");
            StatusLine = $"Ошибка фильтра: {ex.Message}";
        }
    }

    private void RebuildPreviewFlow()
    {
        if (SelectedMemory is null)
        {
            PreviewFlow = new FlowDocument();
            return;
        }

        var fg = (Brush)new BrushConverter().ConvertFromString("#E5E7EB")!;
        PreviewFlow = MarkdownFlowPresenter.Create(
            string.IsNullOrEmpty(SelectedMemory.RawMarkdown) ? SelectedMemory.Content : SelectedMemory.RawMarkdown,
            fg,
            13.5);
    }

    private void OpenVaultFolder()
    {
        try
        {
            var p = _brain.ResolveEffectiveMemoryPath();
            if (Directory.Exists(p))
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = p,
                        UseShellExecute = true,
                    });
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[external-brain-ui] open folder: {ex.Message}");
        }
    }
}

public sealed record NamedTimePreset(TimeFilterPreset Value, string Label);

public enum TimeFilterPreset
{
    All,
    Today,
    Week,
}
