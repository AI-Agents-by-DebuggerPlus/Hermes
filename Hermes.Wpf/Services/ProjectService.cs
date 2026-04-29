using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class ProjectService
{
    public string ConvertToWslPath(string windowsPath)
    {
        if (string.IsNullOrEmpty(windowsPath))
        {
            return string.Empty;
        }

        var normalized = windowsPath.Replace("\\", "/");
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            var drive = char.ToLower(normalized[0]);
            var rest = normalized[2..].TrimStart('/');
            return $"/mnt/{drive}/{rest}";
        }

        return normalized;
    }

    public HermesProject BuildProject(string path)
    {
        var cleanPath = path.Trim();
        return new HermesProject
        {
            Name = System.IO.Path.GetFileName(cleanPath),
            WindowsPath = cleanPath
        };
    }
}
