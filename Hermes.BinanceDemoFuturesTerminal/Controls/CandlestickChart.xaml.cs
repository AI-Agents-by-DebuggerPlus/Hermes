using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Controls;

public partial class CandlestickChart : UserControl
{
    public static readonly DependencyProperty CandlesProperty =
        DependencyProperty.Register(
            nameof(Candles),
            typeof(IEnumerable<Candle>),
            typeof(CandlestickChart),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LastPriceProperty =
        DependencyProperty.Register(
            nameof(LastPrice),
            typeof(double),
            typeof(CandlestickChart),
            new PropertyMetadata(0d));

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

    public CandlestickChart()
    {
        InitializeComponent();
    }
}
