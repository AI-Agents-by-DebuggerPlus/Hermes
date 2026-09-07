using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>
/// AndroidChat ≥ 1.0.46 camera photo in <c>public.messages.content</c>
/// (<c>type=file</c>, <c>kind=photo</c>, Storage <c>chat-files</c>).
/// </summary>
public sealed class AndroidChatPhotoPayload
{
    public required string Name { get; init; }
    public required string Bucket { get; init; }
    public required string Path { get; init; }
    public string Mime { get; init; } = "image/jpeg";
    public string? Nonce { get; init; }
    public long? Size { get; init; }

    /// <summary>
    /// Camera photo if <c>kind=photo</c>, or <c>type=file</c> + <c>image/*</c> from AndroidChat.
    /// Not XML/zip file JSON, not <c>hwt_screenshot</c>.
    /// </summary>
    public static bool TryParse(string? content, string? senderName, out AndroidChatPhotoPayload payload)
    {
        payload = null!;
        var t = (content ?? string.Empty).Trim();
        if (t.Length < 10 || t[0] != '{')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(t);
            var root = doc.RootElement;
            var type = GetString(root, "type");
            if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var kind = GetString(root, "kind");
            var mime = GetString(root, "mime") ?? string.Empty;
            var isPhotoKind = string.Equals(kind, "photo", StringComparison.OrdinalIgnoreCase);
            var isAndroidImage = string.Equals((senderName ?? string.Empty).Trim(), "AndroidChat", StringComparison.OrdinalIgnoreCase)
                                 && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            if (!isPhotoKind && !isAndroidImage)
            {
                return false;
            }

            var path = GetString(root, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var name = GetString(root, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = System.IO.Path.GetFileName(path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "photo.jpg";
            }

            long? size = null;
            if (root.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var sz))
            {
                size = sz;
            }

            var bucket = GetString(root, "bucket");
            payload = new AndroidChatPhotoPayload
            {
                Name = name.Trim(),
                Bucket = string.IsNullOrWhiteSpace(bucket) ? "chat-files" : bucket.Trim(),
                Path = path.Trim(),
                Mime = string.IsNullOrWhiteSpace(mime) ? "image/jpeg" : mime.Trim(),
                Nonce = GetString(root, "nonce")?.Trim(),
                Size = size,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>URL-encode each path segment; keep slashes (Storage authenticated GET).</summary>
    public static string EncodeStorageObjectPath(string path) =>
        string.Join("/", (path ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
