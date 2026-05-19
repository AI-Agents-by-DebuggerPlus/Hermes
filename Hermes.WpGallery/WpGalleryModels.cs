namespace Hermes.WpGallery;

public sealed record WpGalleryImageFrame(
    byte[] Data,
    string MimeType,
    string Filename,
    DateTime CapturedAt,
    int Width,
    int Height,
    IReadOnlyDictionary<string, object?>? ExtraMeta = null);

public sealed record WpGalleryUploadResult(
    bool Success,
    string Message,
    string ImageUrl = "",
    int ImageId = 0,
    long BytesSent = 0,
    double ElapsedMs = 0);

public sealed record WpGalleryConnectionTestResult(bool Ok, string Message);

public sealed class WpGalleryUploadOptions
{
    /// <summary>Site base URL or full REST endpoint.</summary>
    public required string SiteOrImageEndpoint { get; init; }

    /// <summary>Maps to JSON <c>sender</c> (channel in WP gallery). Empty/default → machine name.</summary>
    public string? Sender { get; init; }

    /// <summary>Alias for <see cref="Sender"/>.</summary>
    public string Channel { get; init; } = "default";

    public int MaxRetries { get; init; } = 3;
}

public sealed class WpGalleryConnectionOptions
{
    public required string SiteOrImageEndpoint { get; init; }

    public string? Sender { get; init; }

    public string Channel { get; init; } = "default";
}

public sealed class WpGalleryWebSocketOptions
{
    public required string WebSocketUrl { get; init; }

    public string? Sender { get; init; }

    public string Channel { get; init; } = "default";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
