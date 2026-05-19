using System.IO;
using Hermes.DesktopCapture.Models;
using Hermes.WpGallery;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Publishes images via POST /hermes/v1/message (hermes-image-receiver).</summary>
public sealed class HermesGalleryPublisher
{
    private readonly WpGalleryClient _client = new();
    private readonly Func<HermesSettings> _settings;
    private readonly LogService _log;

    public HermesGalleryPublisher(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsAutoPublishEnabled
    {
        get
        {
            var s = _settings();
            return s.HermesGalleryPublishEnabled && SiteIsConfigured(s);
        }
    }

    public bool HasSiteConfigured => SiteIsConfigured(_settings());

    public async Task<bool> TryPublishPlainScreenshotAsync(
        ScreenCaptureResult capture,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings();
        if (!SiteIsConfigured(settings))
        {
            _log.LogInfo("[wp-gallery] agent: skip — URL сайта WordPress не задан");
            return false;
        }

        if (!settings.HermesGalleryPublishEnabled)
        {
            _log.LogInfo(
                "[wp-gallery] agent: skip — автопубликация выключена (окно WordPress → «После скриншота агента…»)");
            return false;
        }

        var plainPath = capture.ImagePath;
        if (string.IsNullOrWhiteSpace(plainPath) || !File.Exists(plainPath))
        {
            _log.LogWarn($"[wp-gallery] agent: plain PNG not found ({plainPath ?? "null"})");
            return false;
        }

        _log.LogInfo($"[wp-gallery] agent: uploading {Path.GetFileName(plainPath)}…");
        var result = await UploadFileAsync(plainPath, cancellationToken, "agent").ConfigureAwait(false);
        return result.Success;
    }

    public async Task<WpGalleryUploadResult> UploadFileAsync(
        string filePath,
        CancellationToken cancellationToken = default,
        string source = "manual")
    {
        var settings = _settings();
        if (!SiteIsConfigured(settings))
        {
            return new WpGalleryUploadResult(false, "URL сайта WordPress не задан");
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new WpGalleryUploadResult(false, "Файл не найден");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[wp-gallery] {source}: read failed — {ex.Message}");
            return new WpGalleryUploadResult(false, $"Не удалось прочитать файл: {ex.Message}");
        }

        if (bytes.Length < 10)
        {
            return new WpGalleryUploadResult(false, "Файл слишком маленький для изображения");
        }

        var frame = new WpGalleryImageFrame(
            bytes,
            MimeFromExtension(filePath),
            Path.GetFileName(filePath),
            DateTime.UtcNow,
            0,
            0);

        var endpoint = ResolveSiteOrEndpoint(settings);
        var sender = WpGalleryEndpoints.EffectiveSender(settings.HermesGalleryChannel);

        var result = await _client.UploadAsync(
            frame,
            new WpGalleryUploadOptions
            {
                SiteOrImageEndpoint = endpoint,
                Sender = sender,
                MaxRetries = settings.HermesGalleryMaxRetries,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            _log.LogInfo($"[wp-gallery] {source}: POST /message ok, sender={sender} → {result.ImageUrl}");
        }
        else
        {
            _log.LogWarn($"[wp-gallery] {source}: upload failed — {result.Message}");
        }

        return result;
    }

    public async Task<WpGalleryConnectionTestResult> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var s = _settings();
        var endpoint = ResolveSiteOrEndpoint(s);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new WpGalleryConnectionTestResult(false, "URL сайта не задан");
        }

        var test = await _client.TestConnectionAsync(
            new WpGalleryConnectionOptions
            {
                SiteOrImageEndpoint = endpoint,
                Sender = WpGalleryEndpoints.EffectiveSender(s.HermesGalleryChannel),
            },
            cancellationToken).ConfigureAwait(false);

        if (test.Ok)
        {
            _log.LogInfo($"[wp-gallery] test: {test.Message}");
        }
        else
        {
            _log.LogWarn($"[wp-gallery] test: {test.Message}");
        }

        return test;
    }

    private static bool SiteIsConfigured(HermesSettings s) =>
        !string.IsNullOrWhiteSpace(ResolveSiteOrEndpoint(s));

    private static string MimeFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };

    private static string ResolveSiteOrEndpoint(HermesSettings s)
    {
        foreach (var candidate in new[] { s.HermesGallerySiteUrl, s.HermesGalleryRestUrl })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (WpGalleryEndpoints.TryNormalizeSiteUrl(candidate.Trim(), out var site, out _))
            {
                return site;
            }
        }

        return string.Empty;
    }
}
