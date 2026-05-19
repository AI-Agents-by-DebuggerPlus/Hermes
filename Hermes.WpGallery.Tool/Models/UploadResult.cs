namespace Hermes.WpGallery.Tool.Models;

public record UploadResult(
    bool   Success,
    string Message,
    string ImageUrl  = "",
    int    ImageId   = 0,
    long   BytesSent = 0,
    double ElapsedMs = 0
);

public record CaptureFrame(
    byte[]   Data,
    string   MimeType,
    string   Filename,
    DateTime CapturedAt,
    int      Width,
    int      Height
);
