using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Hermes.Wpf.Services;

public static class ProjectAvatarStore
{
    public static string AvatarsRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf",
            "project-avatars");

    public static string ImportAvatar(string sourceImagePath, string projectWindowsPath)
    {
        if (!File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException("Avatar file not found", sourceImagePath);
        }

        Directory.CreateDirectory(AvatarsRoot);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectWindowsPath.Trim().ToLowerInvariant())))[..16];
        var ext = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8)
        {
            ext = ".png";
        }

        var dest = Path.Combine(AvatarsRoot, key + ext.ToLowerInvariant());
        File.Copy(sourceImagePath, dest, overwrite: true);
        return dest;
    }
}
