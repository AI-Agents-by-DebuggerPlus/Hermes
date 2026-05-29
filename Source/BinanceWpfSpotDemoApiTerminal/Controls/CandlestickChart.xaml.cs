using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BinanceWpfSpotDemoApiTerminal.Models;

namespace BinanceWpfSpotDemoApiTerminal.Controls
{
    public partial class CandlestickChart : UserControl
    {
        public static readonly DependencyProperty CandlesProperty =
            DependencyProperty.Register(
                nameof(Candles),
                typeof(IEnumerable<Candle>),
                typeof(CandlestickChart),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnCandlesChanged));

        public IEnumerable<Candle> Candles
        {
            get => (IEnumerable<Candle>)GetValue(CandlesProperty);
            set => SetValue(CandlesProperty, value);
        }

        private static void OnCandlesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CandlestickChart chart)
            {
                if (e.OldValue is INotifyCollectionChanged oldCc)
                {
                    oldCc.CollectionChanged -= chart.OnCollectionChanged;
                }
                if (e.NewValue is INotifyCollectionChanged newCc)
                {
                    newCc.CollectionChanged += chart.OnCollectionChanged;
                }
                chart.InvalidateVisual();
            }
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Обновляем отображение на UI потоке при получении новых тиков
            Dispatcher.BeginInvoke(new Action(InvalidateVisual));
        }

        public CandlestickChart()
        {
            InitializeComponent();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double width = ActualWidth;
            double height = ActualHeight;

            // Заливка фона графика
            var bgBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x1A, 0x20)); // Charcoal Slate
            drawingContext.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

            if (Candles == null || !Candles.Any())
            {
                DrawTextCenter(drawingContext, "Загрузка данных графика...", width, height);
                return;
            }

            var list = Candles.ToList();
            int count = list.Count;

            // Отступы для шкалы цен и времени
            double rightPadding = 65;  // Для цен на оси Y
            double bottomPadding = 25; // Для дат на оси X
            double topPadding = 15;
            double leftPadding = 10;

            double chartWidth = width - leftPadding - rightPadding;
            double chartHeight = height - topPadding - bottomPadding;

            if (chartWidth <= 0 || chartHeight <= 0)
                return;

            // Расчет ценовых минимумов и максимумов для масштабирования
            double maxPrice = list.Max(c => c.High);
            double minPrice = list.Min(c => c.Low);
            double maxVolume = list.Max(c => c.Volume);

            // Небольшой отступ по высоте для красоты
            double priceDiff = maxPrice - minPrice;
            if (priceDiff == 0) priceDiff = 1.0;
            maxPrice += priceDiff * 0.05;
            minPrice -= priceDiff * 0.05;
            priceDiff = maxPrice - minPrice;

            // 1. Отрисовка сетки цен (Горизонтальные линии) и подписей оси Y
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2B, 0x31, 0x39)), 0.5); // Приглушенный серый
            var labelBrush = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C)); // Серебряный
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            int horizontalLinesCount = 5;
            for (int i = 0; i < horizontalLinesCount; i++)
            {
                double ratio = (double)i / (horizontalLinesCount - 1);
                double y = topPadding + chartHeight - ratio * chartHeight;
                double price = minPrice + ratio * priceDiff;

                // Линия сетки
                drawingContext.DrawLine(gridPen, new Point(leftPadding, y), new Point(leftPadding + chartWidth, y));

                // Текст цены
                string priceText = price >= 1000 ? price.ToString("N2") : price.ToString("N4");
                var formattedText = new FormattedText(
                    priceText,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    10,
                    labelBrush,
                    dpi
                );

                drawingContext.DrawText(formattedText, new Point(leftPadding + chartWidth + 5, y - formattedText.Height / 2));
            }

            // 2. Отрисовка временной сетки (Вертикальные линии) и подписей оси X
            int verticalLinesCount = 4;
            int step = count / (verticalLinesCount - 1);
            if (step <= 0) step = 1;

            for (int i = 0; i < verticalLinesCount; i++)
            {
                int index = i * step;
                if (index >= count) index = count - 1;

                double wColumn = chartWidth / count;
                double x = leftPadding + (index * wColumn) + (wColumn / 2);

                // Линия сетки
                drawingContext.DrawLine(gridPen, new Point(x, topPadding), new Point(x, topPadding + chartHeight));

                // Текст времени
                string timeText = list[index].OpenTime.ToString("HH:mm");
                var formattedText = new FormattedText(
                    timeText,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    10,
                    labelBrush,
                    dpi
                );

                drawingContext.DrawText(formattedText, new Point(x - formattedText.Width / 2, topPadding + chartHeight + 5));
            }

            // 3. Отрисовка свечей и объемов
            double colWidth = chartWidth / count;
            double candleBodyWidth = Math.Max(1.5, colWidth * 0.7); // 70% от ширины колонки

            var greenBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0xCB, 0x81)); // Изумрудный для Buy
            var greenPen = new Pen(greenBrush, 1);
            var redBrush = new SolidColorBrush(Color.FromRgb(0xF6, 0x46, 0x5D)); // Малиновый для Sell
            var redPen = new Pen(redBrush, 1);

            // Полупрозрачные кисти для объемов
            var greenVolBrush = new SolidColorBrush(Color.FromArgb(50, 0x0E, 0xCB, 0x81));
            var redVolBrush = new SolidColorBrush(Color.FromArgb(50, 0xF6, 0x46, 0x5D));

            double volumeHeightMax = chartHeight * 0.18; // Объем займет до 18% высоты снизу

            for (int i = 0; i < count; i++)
            {
                var c = list[i];
                double xCenter = leftPadding + (i * colWidth) + (colWidth / 2);

                // Координаты цены Y
                double yOpen = GetY(c.Open, minPrice, priceDiff, chartHeight, topPadding);
                double yClose = GetY(c.Close, minPrice, priceDiff, chartHeight, topPadding);
                double yHigh = GetY(c.High, minPrice, priceDiff, chartHeight, topPadding);
                double yLow = GetY(c.Low, minPrice, priceDiff, chartHeight, topPadding);

                bool isBullish = c.Close >= c.Open;
                var currentBrush = isBullish ? greenBrush : redBrush;
                var currentPen = isBullish ? greenPen : redPen;
                var currentVolBrush = isBullish ? greenVolBrush : redVolBrush;

                // А. Отрисовка гистограммы объемов
                if (maxVolume > 0)
                {
                    double volHeight = (c.Volume / maxVolume) * volumeHeightMax;
                    double yVol = topPadding + chartHeight - volHeight;
                    drawingContext.DrawRectangle(currentVolBrush, null, new Rect(xCenter - candleBodyWidth / 2, yVol, candleBodyWidth, volHeight));
                }

                // Б. Отрисовка фитилей свечи (High-Low)
                drawingContext.DrawLine(currentPen, new Point(xCenter, yHigh), new Point(xCenter, yLow));

                // В. Отрисовка тела свечи (Open-Close)
                double yBodyTop = Math.Min(yOpen, yClose);
                double bodyHeight = Math.Max(1.0, Math.Abs(yOpen - yClose)); // Тело свечи минимум 1 пиксель

                drawingContext.DrawRectangle(currentBrush, null, new Rect(xCenter - candleBodyWidth / 2, yBodyTop, candleBodyWidth, bodyHeight));
            }
        }

        private double GetY(double price, double minPrice, double priceDiff, double chartHeight, double topPadding)
        {
            return topPadding + chartHeight - ((price - minPrice) / priceDiff) * chartHeight;
        }

        private void DrawTextCenter(DrawingContext drawingContext, string text, double width, double height)
        {
            var textBrush = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip
            );

            drawingContext.DrawText(formattedText, new Point((width - formattedText.Width) / 2, (height - formattedText.Height) / 2));
        }
    }
}
