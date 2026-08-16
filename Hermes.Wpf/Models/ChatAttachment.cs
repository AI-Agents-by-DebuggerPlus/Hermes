namespace Hermes.Wpf.Models;

/// <summary>Pending or sent chat attachment (local file under project <c>hermes/attachments</c>).</summary>
public sealed class ChatAttachment
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    };

    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public required string DisplayName { get; init; }
    public bool IsImage { get; init; }
    public long SizeBytes { get; init; }

    public static bool IsImageFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    public static ChatAttachment FromPath(string path) =>
        new()
        {
            Id = path,
            FilePath = path,
            DisplayName = System.IO.Path.GetFileName(path),
            IsImage = IsImageFile(path),
            SizeBytes = 0,
        };
}
