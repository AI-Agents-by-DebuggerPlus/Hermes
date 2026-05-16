using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.ViewModels;

public sealed class WslMemoryViewModel : BaseViewModel
{
    private readonly LogService _log;
    private readonly HermesSettings _settings;
    private readonly ExternalBrainService _externalBrain;
    private readonly WslAgentMemoryService _memoryService;
    private readonly WslAgentMemorySyncService _syncService;

    private readonly ObservableCollection<WslMemoryFileSnapshot> _files = new();

    private WslMemoryFileSnapshot? _selectedFile;
    private string _selectedContent = string.Empty;
    private string _memoriesDirectory = "(не найдено)";
    private string _statusLine = "Нажмите «Обновить», чтобы прочитать ~/.hermes/memories из WSL.";
    private bool _refreshBusy;

    public WslMemoryViewModel(
        LogService log,
        HermesSettings settings,
        ExternalBrainService externalBrain,
        WslAgentMemoryService memoryService,
        WslAgentMemorySyncService syncService)
    {
        _log = log;
        _settings = settings;
        _externalBrain = externalBrain;
        _memoryService = memoryService;
        _syncService = syncService;

        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !_refreshBusy);
        ExportToVaultCommand = new RelayCommand(_ => ExportToVault(), _ => !_refreshBusy);
        OpenFolderCommand = new RelayCommand(_ => OpenFolder(), _ => Directory.Exists(_memoriesDirectory));
    }

    public ObservableCollection<WslMemoryFileSnapshot> Files => _files;

    public WslMemoryFileSnapshot? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (!SetProperty(ref _selectedFile, value))
            {
                return;
            }

            SelectedContent = value is null ? string.Empty : FormatSelectedContent(value);
        }
    }

    public string SelectedContent
    {
        get => _selectedContent;
        private set => SetProperty(ref _selectedContent, value);
    }

    public string MemoriesDirectory
    {
        get => _memoriesDirectory;
        private set => SetProperty(ref _memoriesDirectory, value);
    }

    public string StatusLine
    {
        get => _statusLine;
        private set => SetProperty(ref _statusLine, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand ExportToVaultCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public async Task RefreshAsync()
    {
        if (_refreshBusy)
        {
            return;
        }

        _refreshBusy = true;
        CommandManager.InvalidateRequerySuggested();
        StatusLine = "Чтение WSL memory…";
        try
        {
            var snapshots = await Task.Run(() => _memoryService.LoadSnapshots(_settings)).ConfigureAwait(true);
            var dir = _memoryService.ResolveMemoriesDirectory(_settings);
            MemoriesDirectory = string.IsNullOrWhiteSpace(dir) ? "(не найдено)" : dir!;
            var previous = SelectedFile?.FullPath;
            _files.Clear();
            foreach (var snapshot in snapshots)
            {
                _files.Add(snapshot);
            }

            SelectedFile = previous is null
                ? _files.FirstOrDefault()
                : _files.FirstOrDefault(f => string.Equals(f.FullPath, previous, StringComparison.OrdinalIgnoreCase))
                  ?? _files.FirstOrDefault();

            StatusLine = snapshots.Count == 0
                ? "Файлы USER.md / MEMORY.md не найдены в WSL. Проверьте дистрибутив в Settings и что агент уже писал в ~/.hermes/memories."
                : $"Показано файлов: {snapshots.Count}. Каталог: {MemoriesDirectory}";
        }
        catch (Exception ex)
        {
            _log.LogError($"[wsl-memory-ui] refresh: {ex.Message}");
            StatusLine = $"Ошибка чтения: {ex.Message}";
        }
        finally
        {
            _refreshBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ExportToVault()
    {
        try
        {
            var result = _syncService.TrySync(_settings, _externalBrain);
            StatusLine = result.DidUpdate
                ? $"Экспорт в vault: {result.Detail}"
                : $"Экспорт не выполнен: {result.Detail}";
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            _log.LogError($"[wsl-memory-ui] export: {ex.Message}");
            StatusLine = $"Ошибка экспорта: {ex.Message}";
        }
    }

    private void OpenFolder()
    {
        try
        {
            if (!Directory.Exists(MemoriesDirectory))
            {
                return;
            }

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = MemoriesDirectory,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            _log.LogError($"[wsl-memory-ui] open folder: {ex.Message}");
            StatusLine = $"Не удалось открыть папку: {ex.Message}";
        }
    }

    private static string FormatSelectedContent(WslMemoryFileSnapshot snapshot)
    {
        if (snapshot.Entries.Count <= 1)
        {
            return snapshot.RawContent;
        }

        var blocks = snapshot.Entries
            .Select((entry, index) => $"## Entry {index + 1}{Environment.NewLine}{Environment.NewLine}{entry}");
        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }
}
