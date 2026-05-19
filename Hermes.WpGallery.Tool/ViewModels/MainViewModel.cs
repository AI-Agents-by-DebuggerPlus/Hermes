using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using System.IO;
using Hermes.WpGallery.Tool.Models;
using Hermes.WpGallery.Tool.Services;

namespace Hermes.WpGallery.Tool.ViewModels;

public class MainViewModel : BaseViewModel, IDisposable
{
    private readonly SettingsService      _settings;
    private readonly WordPressService     _wp;
    private readonly ScreenCaptureService _capture;
    private readonly FileLogService         _fileLog;
    private readonly WordPressLogSyncService _wpLogSync;
    private HermesLogSender?                _remoteLog;

    private bool _disposed;

    // ── Stats ──────────────────────────────────────────────
    private int    _totalSent;
    private int    _totalErrors;
    private long   _totalBytes;
    private double _lastElapsedMs;

    // ── Bindable properties ────────────────────────────────
    private bool   _isBusy;
    private string _statusText  = "Готов";
    private string _statusColor = "#64748b";
    private BitmapImage? _previewImage;

    public bool   IsBusy        { get => _isBusy;        set => SetField(ref _isBusy, value); }
    public string StatusText    { get => _statusText;     set => SetField(ref _statusText, value); }
    public string StatusColor   { get => _statusColor;    set => SetField(ref _statusColor, value); }
    public BitmapImage? PreviewImage { get => _previewImage; set => SetField(ref _previewImage, value); }

    public string LogsFolder      => _fileLog.LogsDirectory;
    public string WordPressFolder => _wpLogSync.WordPressDirectory;

    // Stats display
    public string StatSent    => $"{_totalSent}";
    public string StatErrors  => $"{_totalErrors}";
    public string StatBytes   => FormatBytes(_totalBytes);
    public string StatLatency => _lastElapsedMs > 0 ? $"{_lastElapsedMs:F0} мс" : "—";

    // Monitors list
    public List<string> Monitors => ScreenCaptureService.GetMonitors();

    // Settings (bound directly)
    public AppSettings Settings => _settings.Current;

    // Log
    public ObservableCollection<LogEntry> Log { get; } = new();

    // Commands
    public AsyncRelayCommand CaptureNowCommand  { get; }
    public AsyncRelayCommand TestConnCommand    { get; }
    public RelayCommand      ClearLogCommand    { get; }
    public RelayCommand      SaveSettingsCommand{ get; }
    public RelayCommand      OpenSiteCommand    { get; }
    public RelayCommand      OpenLogsFolderCommand { get; }
    public RelayCommand      OpenWordPressFolderCommand { get; }
    public AsyncRelayCommand SyncSiteLogsCommand { get; }

    // ── Constructor ────────────────────────────────────────
    public MainViewModel(
        SettingsService settings,
        WordPressService wp,
        ScreenCaptureService capture,
        FileLogService fileLog,
        WordPressLogSyncService wpLogSync)
    {
        _settings  = settings;
        _wp        = wp;
        _capture   = capture;
        _fileLog   = fileLog;
        _wpLogSync = wpLogSync;

        CaptureNowCommand         = new AsyncRelayCommand(CaptureAndUploadAsync);
        TestConnCommand           = new AsyncRelayCommand(TestConnectionAsync);
        ClearLogCommand           = new RelayCommand(() => Log.Clear());
        SaveSettingsCommand       = new RelayCommand(SaveSettings);
        OpenSiteCommand           = new RelayCommand(OpenSite);
        OpenLogsFolderCommand     = new RelayCommand(OpenLogsFolder);
        OpenWordPressFolderCommand = new RelayCommand(OpenWordPressFolder);
        SyncSiteLogsCommand       = new AsyncRelayCommand(SyncSiteLogsAsync);

        UpdateRemoteLogSender();
        AddLog(LogLevel.Info, "Hermes.WpGallery.Tool запущен");
        AddLog(LogLevel.Info, $"Локальный журнал: {_fileLog.LogsDirectory}");
        AddLog(LogLevel.Info, $"Логи с сайта: {_wpLogSync.WordPressDirectory}");
        _ = SyncSiteLogsAsync();
    }

    private void UpdateRemoteLogSender()
    {
        _remoteLog?.Dispose();
        _remoteLog = null;
        if (Settings.SendLogsToSite && HasUploadCredentials)
        {
            _remoteLog = new HermesLogSender(Settings.WordPressUrl, Settings.SecretToken);
        }
    }

    private bool HasUploadCredentials =>
        !string.IsNullOrWhiteSpace(Settings.WordPressUrl)
        && !string.IsNullOrWhiteSpace(Settings.SecretToken);

    // ── Single capture + upload (только по кнопке) ─────────
    private async Task CaptureAndUploadAsync()
    {
        if (IsBusy)
        {
            AddLog(LogLevel.Info, "Подождите — выполняется предыдущая операция");
            return;
        }
        IsBusy = true;
        try
        {
            CaptureFrame frame;
            try
            {
                frame = await Task.Run(() => _capture.Capture(Settings));
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Ошибка захвата: {ex.Message}");
                SetStatus("Ошибка захвата", "#ef4444");
                return;
            }

            string savedPath;
            try
            {
                savedPath = _fileLog.SaveScreenshot(frame.Data, frame.Filename);
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Снимок не сохранён на диск: {ex.Message}");
                savedPath = "";
            }

            if (Settings.ShowPreview)
                UpdatePreview(frame.Data);

            AddLog(LogLevel.Info,
                $"Снимок: {frame.Width}×{frame.Height}, {FormatBytes(frame.Data.Length)}"
                + (savedPath != "" ? $" → {savedPath}" : ""));

            if (!HasUploadCredentials)
            {
                SetStatus("Снимок готов (не отправлен)", "#f59e0b");
                AddLog(LogLevel.Warning,
                    "Укажите URL сайта и токен в «Настройках», чтобы отправлять на сайт");
                return;
            }

            SetStatus("Загрузка…", "#f59e0b");
            var result = await _wp.UploadAsync(frame);

            _lastElapsedMs = result.ElapsedMs;
            _totalBytes   += result.BytesSent;

            if (result.Success)
            {
                _totalSent++;
                var ch = WordPressService.GetEffectiveSender(Settings);
                SetStatus("Загружено ✓", "#22c55e");
                AddLog(LogLevel.Success,
                    $"{frame.Filename}  {FormatBytes(frame.Data.Length)}  {result.ElapsedMs:F0} мс  [канал: {ch}]");
                AddLog(LogLevel.Info,
                    "На странице шорткод должен быть: [hermes_gallery channel=\"" + ch + "\"]");
            }
            else
            {
                _totalErrors++;
                SetStatus("Ошибка загрузки", "#ef4444");
                AddLog(LogLevel.Error, result.Message);
            }

            NotifyStats();
        }
        finally { IsBusy = false; }
    }

    // ── Connection test ────────────────────────────────────
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        SetStatus("Проверка соединения…", "#f59e0b");
        AddLog(LogLevel.Info, "Тест соединения…");
        var (ok, msg) = await _wp.TestConnectionAsync();
        if (ok)
        {
            SetStatus("Соединение OK", "#22c55e");
            AddLog(LogLevel.Success, msg);
        }
        else
        {
            SetStatus("Ошибка соединения", "#ef4444");
            AddLog(LogLevel.Error, msg);
        }
        IsBusy = false;
    }

    // ── Helpers ────────────────────────────────────────────
    private void SaveSettings()
    {
        _settings.Save();
        UpdateRemoteLogSender();
        AddLog(LogLevel.Info, "Настройки сохранены");
    }

    private void OpenSite()
    {
        var url = Settings.WordPressUrl.TrimEnd('/');
        if (!string.IsNullOrEmpty(url))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenLogsFolder()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_fileLog.LogsDirectory)
        {
            UseShellExecute = true
        });
    }

    private void OpenWordPressFolder()
    {
        Directory.CreateDirectory(_wpLogSync.WordPressDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_wpLogSync.WordPressDirectory)
        {
            UseShellExecute = true
        });
    }

    private async Task SyncSiteLogsAsync()
    {
        if (!HasUploadCredentials) return;
        var (ok, msg, _) = await _wpLogSync.SyncFromSiteAsync();
        if (ok)
            AddLog(LogLevel.Info, msg);
        else
            AddLog(LogLevel.Warning, $"Логи с сайта: {msg}");
    }

    private void SetStatus(string text, string color)
        => App.Current.Dispatcher.Invoke(() => { StatusText = text; StatusColor = color; });

    private void AddLog(LogLevel level, string msg)
    {
        _fileLog.Write(level, msg);
        _remoteLog?.Log(level.ToString(), msg, "Hermes.WpGallery.Tool");
        var entry = new LogEntry { Level = level, Message = msg };
        App.Current.Dispatcher.Invoke(() =>
        {
            Log.Insert(0, entry);
            if (Log.Count > 200) Log.RemoveAt(Log.Count - 1);
        });
    }

    private void NotifyStats()
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(StatSent));
            OnPropertyChanged(nameof(StatErrors));
            OnPropertyChanged(nameof(StatBytes));
            OnPropertyChanged(nameof(StatLatency));
        });
    }

    private void UpdatePreview(byte[] data)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                using var ms = new MemoryStream(data);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource       = ms;
                bmp.CacheOption        = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth   = 480;
                bmp.CreateOptions      = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage = bmp;
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Предпросмотр: {ex.Message}");
            }
        });
    }

    private static string FormatBytes(long b) => b switch
    {
        < 1024             => $"{b} Б",
        < 1024 * 1024      => $"{b / 1024.0:F1} КБ",
        _                  => $"{b / 1024.0 / 1024.0:F2} МБ",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _remoteLog?.Dispose();
        _remoteLog = null;
    }
}
