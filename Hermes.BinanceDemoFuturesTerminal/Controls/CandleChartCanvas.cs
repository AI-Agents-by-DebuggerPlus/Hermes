using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Controls;

public sealed class CandleChartCanvas : FrameworkElement
{
    public static readonly DependencyProperty CandlesProperty =
        DependencyProperty.Register(
            nameof(Candles),
            typeof(IEnumerable<Candle>),
            typeof(CandleChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnCandlesChanged));

    public static readonly DependencyProperty LastPriceProperty =
        DependencyProperty.Register(
            nameof(LastPrice),
            typeof(double),
            typeof(CandleChartCanvas),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<Candle>? Candles
    {
        get => (IEnumerable<Candle>?)GetValue(CandlesProperty);
        set => SetValue(CandlesProperty, value);
    }

    public double LastPrice
    {
        get => (double)GetValue(LastPriceProperty);
        set => SetValue(LastPriceProperty, value);
    }

    public CandleChartCanvas()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    private static void OnCandlesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CandleChartCanvas canvas)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged oldCc)
        {
            oldCc.CollectionChanged -= canvas.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCc)
        {
            newCc.CollectionChanged += canvas.OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bgBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x1A, 0x20));
        drawingContext.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

        if (Candles == null || !Candles.Any())
        {
            DrawTextCenter(drawingContext, "Загрузка данных графика...", width, height);
            return;
        }

        var list = Candles.ToList();
        var count = list.Count;

        const double rightPadding = 72;
        const double bottomPadding = 28;
        const double topPadding = 12;
        const double leftPadding = 8;

        var chartWidth = width - leftPadding - rightPadding;
        var chartHeight = height - topPadding - bottomPadding;
        if (chartWidth <= 0 || chartHeight <= 0)
        {
            return;
        }

        var maxPrice = list.Max(c => c.High);
        var minPrice = list.Min(c => c.Low);
        var maxVolume = list.Max(c => c.Volume);

        var priceDiff = maxPrice - minPrice;
        if (priceDiff <= 0)
        {
            priceDiff = Math.Max(maxPrice * 0.001, 1.0);
            maxPrice += priceDiff * 0.5;
            minPrice -= priceDiff * 0.5;
        }
        else
        {
            maxPrice += priceDiff * 0.05;
            minPrice -= priceDiff * 0.05;
            priceDiff = maxPrice - minPrice;
        }

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2B, 0x31, 0x39)), 0.5);
        gridPen.Freeze();
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
        labelBrush.Freeze();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        const int horizontalLinesCount = 6;
        for (var i = 0; i < horizontalLinesCount; i++)
        {
            var ratio = (double)i / (horizontalLinesCount - 1);
            var y = topPadding + chartHeight - ratio * chartHeight;
            var price = minPrice + ratio * priceDiff;

            drawingContext.DrawLine(gridPen, new Point(leftPadding, y), new Point(leftPadding + chartWidth, y));
            DrawPriceLabel(drawingContext, price, leftPadding + chartWidth + 6, y, labelBrush, dpi);
        }

        var verticalLinesCount = Math.Min(6, count);
        var step = Math.Max(1, count / Math.Max(1, verticalLinesCount - 1));
        for (var i = 0; i < verticalLinesCount; i++)
        {
            var index = Math.Min(i * step, count - 1);
            var colWidth = chartWidth / count;
            var x = leftPadding + index * colWidth + colWidth / 2;

            drawingContext.DrawLine(gridPen, new Point(x, topPadding), new Point(x, topPadding + chartHeight));
            DrawTimeLabel(drawingContext, list[index].OpenTime, x, topPadding + chartHeight + 6, labelBrush, dpi);
        }

        var greenBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0xCB, 0x81));
        var greenPen = new Pen(greenBrush, 1);
        var redBrush = new SolidColorBrush(Color.FromRgb(0xF6, 0x46, 0x5D));
        var redPen = new Pen(redBrush, 1);
        greenBrush.Freeze();
        redBrush.Freeze();
        greenPen.Freeze();
        redPen.Freeze();

        var greenVolBrush = new SolidColorBrush(Color.FromArgb(45, 0x0E, 0xCB, 0x81));
        var redVolBrush = new SolidColorBrush(Color.FromArgb(45, 0xF6, 0x46, 0x5D));
        greenVolBrush.Freeze();
        redVolBrush.Freeze();

        var colWidthCandle = chartWidth / count;
        var candleBodyWidth = Math.Max(2.0, colWidthCandle * 0.72);
        var volumeHeightMax = chartHeight * 0.16;

        for (var i = 0; i < count; i++)
        {
            var c = list[i];
            var xCenter = leftPadding + i * colWidthCandle + colWidthCandle / 2;

            var yOpen = GetY(c.Open, minPrice, priceDiff, chartHeight, topPadding);
            var yClose = GetY(c.Close, minPrice, priceDiff, chartHeight, topPadding);
            var yHigh = GetY(c.High, minPrice, priceDiff, chartHeight, topPadding);
            var yLow = GetY(c.Low, minPrice, priceDiff, chartHeight, topPadding);

            var isBullish = c.Close >= c.Open;
            var currentBrush = isBullish ? greenBrush : redBrush;
            var currentPen = isBullish ? greenPen : redPen;
            var currentVolBrush = isBullish ? greenVolBrush : redVolBrush;

            if (maxVolume > 0)
            {
                var volHeight = c.Volume / maxVolume * volumeHeightMax;
                var yVol = topPadding + chartHeight - volHeight;
                drawingContext.DrawRectangle(
                    currentVolBrush,
                    null,
                    new Rect(xCenter - candleBodyWidth / 2, yVol, candleBodyWidth, volHeight));
            }

            drawingContext.DrawLine(currentPen, new Point(xCenter, yHigh), new Point(xCenter, yLow));

            var yBodyTop = Math.Min(yOpen, yClose);
            var bodyHeight = Math.Max(1.0, Math.Abs(yOpen - yClose));
            drawingContext.DrawRectangle(
                currentBrush,
                null,
                new Rect(xCenter - candleBodyWidth / 2, yBodyTop, candleBodyWidth, bodyHeight));
        }

        var referencePrice = LastPrice > 0 ? LastPrice : list[^1].Close;
        if (referencePrice >= minPrice && referencePrice <= maxPrice)
        {
            var yLast = GetY(referencePrice, minPrice, priceDiff, chartHeight, topPadding);
            var lastPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF0, 0xB9, 0x0B)), 1)
            {
                DashStyle = DashStyles.Dash,
            };
            lastPen.Freeze();
            drawingContext.DrawLine(lastPen, new Point(leftPadding, yLast), new Point(leftPadding + chartWidth, yLast));

            var lastBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xB9, 0x0B));
            lastBg.Freeze();
            var lastLabel = FormatPrice(referencePrice);
            var lastText = new FormattedText(
                lastLabel,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                Brushes.Black,
                dpi);
            var tagWidth = lastText.Width + 8;
            var tagHeight = lastText.Height + 4;
            drawingContext.DrawRectangle(lastBg, null, new Rect(leftPadding + chartWidth + 2, yLast - tagHeight / 2, tagWidth, tagHeight));
            drawingContext.DrawText(lastText, new Point(leftPadding + chartWidth + 6, yLast - lastText.Height / 2));
        }
    }

    private static double GetY(double price, double minPrice, double priceDiff, double chartHeight, double topPadding) =>
        topPadding + chartHeight - (price - minPrice) / priceDiff * chartHeight;

    private static string FormatPrice(double price) =>
        price >= 1000 ? price.ToString("N2", CultureInfo.InvariantCulture) : price.ToString("N4", CultureInfo.InvariantCulture);

    private static void DrawPriceLabel(DrawingContext dc, double price, double x, double y, Brush brush, double dpi)
    {
        var text = new FormattedText(
            FormatPrice(price),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            brush,
            dpi);
        dc.DrawText(text, new Point(x, y - text.Height / 2));
    }

    private static void DrawTimeLabel(DrawingContext dc, DateTime time, double x, double y, Brush brush, double dpi)
    {
        var text = new FormattedText(
            time.ToString("HH:mm"),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            brush,
            dpi);
        dc.DrawText(text, new Point(x - text.Width / 2, y));
    }

    private static void DrawTextCenter(DrawingContext dc, string text, double width, double height)
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
        brush.Freeze();
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            brush,
            1.0);
        dc.DrawText(formattedText, new Point((width - formattedText.Width) / 2, (height - formattedText.Height) / 2));
    }
}
