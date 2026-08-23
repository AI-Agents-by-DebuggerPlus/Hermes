using System.Globalization;

namespace Hermes.Wpf.Models;

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public required string Text { get; init; }

    /// <summary>Local image file for inline preview (first image attachment or assistant screenshot).</summary>
    public string? ImagePath { get; init; }

    /// <summary>Local paths of files attached to this turn (under <c>hermes/attachments</c>).</summary>
    public IReadOnlyList<string>? AttachmentPaths { get; init; }

    /// <summary>Rich attachments for chip-style preview (same UI as pending before send).</summary>
    public IReadOnlyList<ChatAttachment>? Attachments { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>Local time for chat header: today → HH:mm, else yyyy-MM-dd HH:mm.</summary>
    public string DisplayTimestamp
    {
        get
        {
            var local = Timestamp.Kind == DateTimeKind.Utc ? Timestamp.ToLocalTime() : Timestamp;
            if (local.Date == DateTime.Today)
            {
                return local.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Attachments to render as chips; falls back to paths / single <see cref="ImagePath"/>.</summary>
    public IReadOnlyList<ChatAttachment> PreviewAttachments
    {
        get
        {
            if (Attachments is { Count: > 0 })
            {
                return Attachments;
            }

            if (AttachmentPaths is { Count: > 0 })
            {
                return AttachmentPaths.Select(ChatAttachment.FromPath).ToList();
            }

            if (!string.IsNullOrWhiteSpace(ImagePath))
            {
                return [ChatAttachment.FromPath(ImagePath)];
            }

            return [];
        }
    }

    public bool HasPreviewAttachments => PreviewAttachments.Count > 0;
}
