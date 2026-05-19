using System.IO;
using System.Windows.Input;
using System.Windows;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.WpGallery;

namespace Hermes.Wpf.ViewModels;

public sealed class WordPressGalleryViewModel : BaseViewModel
{
    private readonly HermesSettings _settings;
    private readonly HermesGalleryPublisher _publisher;
    private readonly Action<string> _pickImageFile;
    private bool _publishEnabled;
    private string _siteUrl = string.Empty;
    private string _senderChannel = string.Empty;
    private string _selectedFilePath = string.Empty;
    private string _status = "Укажите URL сайта и при необходимости выберите файл для тестовой отправки.";
    private bool _busy;

    public WordPressGalleryViewModel(
        HermesSettings settings,
        HermesGalleryPublisher publisher,
        Action<string> pickImageFile)
    {
        _settings = settings;
        _publisher = publisher;
        _pickImageFile = pickImageFile;
        _publishEnabled = settings.HermesGalleryPublishEnabled;
        _siteUrl = settings.HermesGallerySiteUrl ?? string.Empty;
        _senderChannel = settings.HermesGalleryChannel ?? string.Empty;

        BrowseFileCommand = new RelayCommand(_ => BrowseFile(), _ => !_busy);
        TestConnectionCommand = new RelayCommand(_ => _ = TestConnectionAsync(), _ => CanTest());
        SendImageCommand = new RelayCommand(_ => _ = SendImageAsync(), _ => CanSend());
    }

    public string EffectiveSender => WpGalleryEndpoints.EffectiveSender(SenderChannel);

    public bool PublishEnabled
    {
        get => _publishEnabled;
        set
        {
            _settings.HermesGalleryPublishEnabled = value;
            SetProperty(ref _publishEnabled, value);
        }
    }

    public string SiteUrl
    {
        get => _siteUrl;
        set
        {
            var v = value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(v)
                && WpGalleryEndpoints.TryNormalizeSiteUrl(v.Trim(), out var site, out _))
            {
                v = site;
            }

            _settings.HermesGallerySiteUrl = v;
            SetProperty(ref _siteUrl, v);
            RaisePropertyChanged(nameof(EffectiveSender));
        }
    }

    public string SenderChannel
    {
        get => _senderChannel;
        set
        {
            var v = value ?? string.Empty;
            _settings.HermesGalleryChannel = v;
            SetProperty(ref _senderChannel, v);
            RaisePropertyChanged(nameof(EffectiveSender));
        }
    }

    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set => SetProperty(ref _selectedFilePath, value ?? string.Empty);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand BrowseFileCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand SendImageCommand { get; }

    public void BrowseFile()
    {
        var startDir = string.IsNullOrWhiteSpace(SelectedFilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : Path.GetDirectoryName(SelectedFilePath);

        _pickImageFile(startDir ?? string.Empty);
    }

    public void SetSelectedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        SelectedFilePath = path;
        Status = $"Выбран: {Path.GetFileName(path)}";
    }

    public async Task TestConnectionAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        Status = "Проверка связи…";
        try
        {
            var result = await _publisher.TestConnectionAsync().ConfigureAwait(true);
            Status = result.Message;
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task SendImageAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        Status = "Отправка…";
        try
        {
            var result = await _publisher.UploadFileAsync(SelectedFilePath).ConfigureAwait(true);
            if (result.Success)
            {
                PublishEnabled = true;
            }

            Status = result.Success
                ? $"Отправлено ({result.BytesSent} байт). {result.ImageUrl}"
                : result.Message;
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool CanTest() =>
        !_busy && !string.IsNullOrWhiteSpace(SiteUrl);

    private bool CanSend() =>
        !_busy
        && !string.IsNullOrWhiteSpace(SiteUrl)
        && !string.IsNullOrWhiteSpace(SelectedFilePath)
        && File.Exists(SelectedFilePath);
}
