using System.IO;
using System.Windows.Media.Imaging;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Copies user files/screenshots into <c>{project}/hermes/attachments/</c> for Hermes CLI.
/// </summary>
public static class ChatAttachmentStore
{
    public static string GetAttachmentsDirectory(string projectWindowsPath)
    {
        var dir = Path.Combine(projectWindowsPath.Trim(), "hermes", "attachments");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool IsImagePath(string path) => ChatAttachment.IsImageFile(path);

    public static ChatAttachment ImportFile(string sourcePath, string projectWindowsPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Файл не найден", sourcePath);
        }

        var dir = GetAttachmentsDirectory(projectWindowsPath);
        var originalName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(originalName))
        {
            originalName = "file.bin";
        }

        var safeName = SanitizeFileName(originalName);
        var destName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{safeName}";
        var destPath = Path.Combine(dir, destName);
        File.Copy(sourcePath, destPath, overwrite: false);

        var info = new FileInfo(destPath);
        return new ChatAttachment
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = destPath,
            DisplayName = originalName,
            IsImage = IsImagePath(destPath),
            SizeBytes = info.Length,
        };
    }

    public static ChatAttachment ImportBitmapSource(BitmapSource bitmap, string projectWindowsPath, string? preferredName = null)
    {
        var dir = GetAttachmentsDirectory(projectWindowsPath);
        var name = string.IsNullOrWhiteSpace(preferredName)
            ? $"paste_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            : SanitizeFileName(preferredName!);
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            name += ".png";
        }

        var destName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{name}";
        var destPath = Path.Combine(dir, destName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var fs = File.Create(destPath))
        {
            encoder.Save(fs);
        }

        var info = new FileInfo(destPath);
        return new ChatAttachment
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = destPath,
            DisplayName = name,
            IsImage = true,
            SizeBytes = info.Length,
        };
    }

    public static ChatAttachment ImportBytes(byte[] bytes, string projectWindowsPath, string fileName)
    {
        var dir = GetAttachmentsDirectory(projectWindowsPath);
        var safe = SanitizeFileName(string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName);
        var destName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{safe}";
        var destPath = Path.Combine(dir, destName);
        File.WriteAllBytes(destPath, bytes);
        var info = new FileInfo(destPath);
        return new ChatAttachment
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = destPath,
            DisplayName = safe,
            IsImage = IsImagePath(destPath),
            SizeBytes = info.Length,
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars);
        return string.IsNullOrWhiteSpace(s) ? "file.bin" : s;
    }
}
