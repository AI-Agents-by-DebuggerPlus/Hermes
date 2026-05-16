using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hermes.Wpf.Views;

public partial class ImageViewerWindow : Window
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;

    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly string _imagePath;
    private bool _suppressSliderSync;
    private bool _fitPending = true;
    private Size _imagePixelSize;

    public ImageViewerWindow(string imagePath)
    {
        _imagePath = imagePath;
        InitializeComponent();

        Photo.RenderTransform = _zoomTransform;
        PathText.Text = imagePath;
        Title = $"Скриншот — {Path.GetFileName(imagePath)}";
        LoadImage();
    }

    private void LoadImage()
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(_imagePath), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        Photo.Source = bitmap;
        _imagePixelSize = new Size(bitmap.PixelWidth, bitmap.PixelHeight);
        SetZoom(1.0, syncSlider: true);
    }

    private void Window_OnContentRendered(object? sender, EventArgs e)
    {
        if (!_fitPending)
        {
            return;
        }

        _fitPending = false;
        ZoomFit_OnClick(this, new RoutedEventArgs());
    }

    private void SetZoom(double scale, bool syncSlider)
    {
        scale = Math.Clamp(scale, MinZoom, MaxZoom);
        _zoomTransform.ScaleX = scale;
        _zoomTransform.ScaleY = scale;
        ZoomLabel.Text = $"{scale * 100:0}%";

        if (!syncSlider)
        {
            return;
        }

        _suppressSliderSync = true;
        try
        {
            ZoomSlider.Value = scale * 100;
        }
        finally
        {
            _suppressSliderSync = false;
        }
    }

    private void ZoomSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderSync || !IsLoaded)
        {
            return;
        }

        SetZoom(ZoomSlider.Value / 100.0, syncSlider: false);
    }

    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) =>
        SetZoom(_zoomTransform.ScaleX * 1.15, syncSlider: true);

    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) =>
        SetZoom(_zoomTransform.ScaleX / 1.15, syncSlider: true);

    private void ZoomReset_OnClick(object sender, RoutedEventArgs e) => SetZoom(1.0, syncSlider: true);

    private void ZoomFit_OnClick(object sender, RoutedEventArgs e)
    {
        if (_imagePixelSize.Width <= 0 || _imagePixelSize.Height <= 0)
        {
            return;
        }

        var availableW = Math.Max(100, Scroller.ActualWidth - 24);
        var availableH = Math.Max(100, Scroller.ActualHeight - 24);
        if (availableW <= 100 || availableH <= 100)
        {
            _fitPending = true;
            return;
        }

        var fit = Math.Min(availableW / _imagePixelSize.Width, availableH / _imagePixelSize.Height);
        SetZoom(Math.Clamp(fit, MinZoom, MaxZoom), syncSlider: true);
    }

    private void Scroller_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        SetZoom(_zoomTransform.ScaleX * factor, syncSlider: true);
        e.Handled = true;
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                ZoomIn_OnClick(this, e);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomOut_OnClick(this, e);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                ZoomReset_OnClick(this, e);
                e.Handled = true;
                break;
            case Key.F:
                ZoomFit_OnClick(this, e);
                e.Handled = true;
                break;
        }
    }
}
