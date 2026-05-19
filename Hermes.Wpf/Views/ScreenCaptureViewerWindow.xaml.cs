using System.IO;
using System.Windows;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Hermes.DesktopCapture.Models;

namespace Hermes.Wpf.Views;

public partial class ScreenCaptureViewerWindow : Window
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;

    private readonly ScreenCaptureResult _capture;
    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly List<RegionListItem> _regionItems = [];
    private readonly Dictionary<string, FrameworkElement> _overlayShapes = new(StringComparer.OrdinalIgnoreCase);

    private bool _suppressSliderSync;
    private bool _fitPending = true;
    private bool _showLiveOverlay;
    private Size _imagePixelSize;
    private string _currentImagePath;

    public ScreenCaptureViewerWindow(ScreenCaptureResult capture, string initialImagePath)
    {
        _capture = capture;
        _currentImagePath = initialImagePath;
        InitializeComponent();

        PhotoHost.RenderTransform = _zoomTransform;
        SummaryText.Text =
            $"{_capture.Monitor.DeviceName}  •  {_capture.Monitor.Width}×{_capture.Monitor.Height}"
            + (string.IsNullOrWhiteSpace(_capture.ForegroundWindowTitle)
                ? string.Empty
                : $"  •  {_capture.ForegroundWindowTitle}");
        PathText.Text = initialImagePath;
        Title = $"Скриншот — {System.IO.Path.GetFileName(initialImagePath)}";

        BuildRegionList();
        LoadImage(initialImagePath);
    }

    private void BuildRegionList()
    {
        foreach (var region in _capture.Regions)
        {
            _regionItems.Add(
                new RegionListItem(
                    region,
                    $"{region.Index}. {region.DisplayName}"));
        }

        RegionList.ItemsSource = _regionItems;
    }

    private void LoadImage(string path)
    {
        _currentImagePath = path;
        PathText.Text = path;
        Title = $"Скриншот — {System.IO.Path.GetFileName(path)}";

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(System.IO.Path.GetFullPath(path), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        Photo.Source = bitmap;
        _imagePixelSize = new Size(bitmap.PixelWidth, bitmap.PixelHeight);
        Overlay.Width = bitmap.PixelWidth;
        Overlay.Height = bitmap.PixelHeight;

        _showLiveOverlay = IsRawCapturePath(path);
        if (_showLiveOverlay)
        {
            RebuildOverlay();
            Overlay.Visibility = Visibility.Visible;
        }
        else
        {
            Overlay.Children.Clear();
            _overlayShapes.Clear();
            Overlay.Visibility = Visibility.Collapsed;
        }

        SetZoom(1.0, syncSlider: true);
        _fitPending = true;
    }

    private bool IsRawCapturePath(string path) =>
        string.Equals(
            System.IO.Path.GetFullPath(path),
            System.IO.Path.GetFullPath(_capture.ImagePath),
            StringComparison.OrdinalIgnoreCase);

    private void RebuildOverlay()
    {
        Overlay.Children.Clear();
        _overlayShapes.Clear();

        foreach (var region in _capture.Regions)
        {
            var local = ToMonitorLocal(region);
            if (local.Width <= 0 || local.Height <= 0)
            {
                continue;
            }

            var brush = BrushForRole(region.Role);
            var rect = new Rectangle
            {
                Width = local.Width,
                Height = local.Height,
                Stroke = brush,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, local.Left);
            Canvas.SetTop(rect, local.Top);
            Overlay.Children.Add(rect);
            _overlayShapes[region.Id] = rect;

            if (region.Index > 0)
            {
                var tag = new Border
                {
                    Background = new SolidColorBrush(MediaColor.FromArgb(210, 20, 20, 20)),
                    Padding = new Thickness(4, 2, 4, 2),
                    Child = new TextBlock
                    {
                        Text = region.Index.ToString(),
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                    },
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(tag, local.Left + 4);
                Canvas.SetTop(tag, local.Top + 4);
                Overlay.Children.Add(tag);
                _overlayShapes[$"{region.Id}_tag"] = tag;
            }
        }
    }

    private Rect ToMonitorLocal(ScreenRegion region)
    {
        var x = region.X - _capture.Monitor.X;
        var y = region.Y - _capture.Monitor.Y;
        var w = region.Width;
        var h = region.Height;
        var maxW = _imagePixelSize.Width;
        var maxH = _imagePixelSize.Height;
        var left = Math.Clamp(x, 0, maxW);
        var top = Math.Clamp(y, 0, maxH);
        var right = Math.Clamp(x + w, 0, maxW);
        var bottom = Math.Clamp(y + h, 0, maxH);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static SolidColorBrush BrushForRole(ScreenRegionRole role) =>
        new(
            role switch
            {
                ScreenRegionRole.ApplicationWindow => MediaColor.FromRgb(160, 160, 160),
                ScreenRegionRole.TitleBar => MediaColors.Gold,
                ScreenRegionRole.MenuBar => MediaColors.Cyan,
                ScreenRegionRole.Editor => MediaColors.LimeGreen,
                ScreenRegionRole.MinimizeButton => MediaColors.Orange,
                ScreenRegionRole.MaximizeButton => MediaColors.MediumPurple,
                ScreenRegionRole.CloseButton => MediaColors.Red,
                ScreenRegionRole.TaskBar => MediaColors.DeepSkyBlue,
                _ => MediaColors.White,
            });

    private void RegionList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showLiveOverlay)
        {
            return;
        }

        foreach (var shape in _overlayShapes.Values)
        {
            shape.Opacity = 0.65;
            if (shape is Rectangle r)
            {
                r.StrokeThickness = 2;
            }
        }

        if (RegionList.SelectedItem is not RegionListItem item)
        {
            return;
        }

        if (!_overlayShapes.TryGetValue(item.Region.Id, out var selected))
        {
            return;
        }

        selected.Opacity = 1.0;
        if (selected is Rectangle rect)
        {
            rect.StrokeThickness = 4;
        }
    }

    private void ShowRaw_OnClick(object sender, RoutedEventArgs e) => LoadImage(_capture.ImagePath);

    private void ShowAnnotated_OnClick(object sender, RoutedEventArgs e) => LoadImage(_capture.AnnotatedImagePath);

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

    private sealed record RegionListItem(ScreenRegion Region, string Detail);
}
