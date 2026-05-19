using Hermes.WpGallery;
using Hermes.WpGallery.Tool.Models;

namespace Hermes.WpGallery.Tool.Services;

/// <summary>Thin adapter over <see cref="WpGalleryClient"/> for the standalone tool UI.</summary>
public sealed class WordPressService
{
    private readonly WpGalleryClient _client;
    private readonly SettingsService _settings;

    public WordPressService(SimpleHttpClientFactory factory, SettingsService settings)
    {
        _client = new WpGalleryClient(factory.CreateClient());
        _settings = settings;
    }

    public static string GetEffectiveSender(AppSettings s) =>
        WpGalleryEndpoints.EffectiveSender(s.Channel);

    public async Task<UploadResult> UploadAsync(CaptureFrame frame, CancellationToken ct = default)
    {
        var s = _settings.Current;
        var result = await _client.UploadAsync(
            ToFrame(frame),
            new WpGalleryUploadOptions
            {
                SiteOrImageEndpoint = s.WordPressUrl,
                Sender = GetEffectiveSender(s),
                MaxRetries = s.MaxRetries,
            },
            ct).ConfigureAwait(false);

        return new UploadResult(
            result.Success,
            result.Message,
            result.ImageUrl,
            result.ImageId,
            result.BytesSent,
            result.ElapsedMs);
    }

    public async Task<(bool ok, string message)> TestConnectionAsync()
    {
        var s = _settings.Current;
        var test = await _client.TestConnectionAsync(
            new WpGalleryConnectionOptions
            {
                SiteOrImageEndpoint = s.WordPressUrl,
                Sender = GetEffectiveSender(s),
            }).ConfigureAwait(false);

        return (test.Ok, test.Message);
    }

    private static WpGalleryImageFrame ToFrame(CaptureFrame frame) =>
        new(
            frame.Data,
            frame.MimeType,
            frame.Filename,
            frame.CapturedAt,
            frame.Width,
            frame.Height);
}
