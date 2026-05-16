using System.IO;
using System.Windows;
using Hermes.Wpf.Views;

namespace Hermes.Wpf.Services;

public static class ImageViewerService
{
    public static bool TryShow(string? imagePath, Window? owner = null)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        var full = Path.GetFullPath(imagePath.Trim());
        if (!File.Exists(full))
        {
            MessageBox.Show(
                owner,
                $"Файл не найден:\n{full}",
                "Скриншот",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var window = new ImageViewerWindow(full) { Owner = owner };
        window.Show();
        return true;
    }
}
