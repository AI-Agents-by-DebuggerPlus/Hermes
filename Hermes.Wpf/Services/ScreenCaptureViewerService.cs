using System.IO;
using System.Windows;
using Hermes.DesktopCapture.Models;
using Hermes.Wpf.Views;

namespace Hermes.Wpf.Services;

public static class ScreenCaptureViewerService
{
    public static bool TryShow(
        ScreenCaptureResult capture,
        bool showAnnotated = true,
        Window? owner = null)
    {
        var path = showAnnotated ? capture.AnnotatedImagePath : capture.ImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            path = capture.ImagePath;
        }

        if (!File.Exists(path))
        {
            MessageBox.Show(
                owner,
                "Файл скриншота не найден.",
                "Скриншот монитора",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var window = new ScreenCaptureViewerWindow(capture, path) { Owner = owner };
        window.Show();
        return true;
    }
}
