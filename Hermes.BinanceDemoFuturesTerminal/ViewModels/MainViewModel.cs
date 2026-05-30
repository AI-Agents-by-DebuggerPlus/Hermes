using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Hermes.BinanceDemoFuturesTerminal.Models;
using Hermes.BinanceDemoFuturesTerminal.MVVM;
using Hermes.BinanceDemoFuturesTerminal.Services;
using Hermes.BinanceDemoFuturesTerminal.Views;

namespace Hermes.BinanceDemoFuturesTerminal.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly BinanceApiService _apiService;
        private readonly BinanceWebSocketService _wsService;

        // API (из настроек)
        private string _apiKey = string.Empty;
        private string _secretKey = string.Empty;
        private string _wsStatus = "Отключено";
        private LogsWindow? _logsWindow;
        private SettingsWindow? _settingsWindow;

        // Поиск и список торговых пар
        private string _searchText = string.Empty;
        private string _selectedSymbol = string.Empty;
        private List<SymbolInfo> _allSymbols = new List<SymbolInfo>();
        private ObservableCollection<string> _filteredSymbols = new ObservableCollection<string>();

        // Рыночные данные (Выбранная пара)
        private TickerModel _ticker;
        private string _spreadDisplay = "Спред: Ожидание данных";
        private ObservableCollection<OrderBookItem> _bids = new ObservableCollection<OrderBookItem>();
        private ObservableCollection<OrderBookItem> _asks = new ObservableCollection<OrderBookItem>();
        private ObservableCollection<TradeModel> _recentTrades = new ObservableCollection<TradeModel>();
        private ObservableCollection<Candle> _candles = new ObservableCollection<Candle>();

        // Данные аккаунта
        private ObservableCollection<BalanceModel> _balances = new ObservableCollection<BalanceModel>();
        private ObservableCollection<PositionModel> _positions = new ObservableCollection<PositionModel>();
        private ObservableCollection<OrderModel> _openOrders = new ObservableCollection<OrderModel>();
        private ObservableCollection<OrderModel> _orderHistory = new ObservableCollection<OrderModel>();

        // Форма ордера
        private string _orderPrice = string.Empty;
        private string _orderQuantity = string.Empty;
        private string _orderStopLoss = string.Empty;
        private string _orderTakeProfit = string.Empty;
        private double _totalCost = 0.0;
        private bool _isLimitOrder = true;
        private bool _quantityInUsdt = true;
        private string _contractQtyPreview = string.Empty;
        private bool _applyMinQtyOnNextPrice;

        #region Свойства
        public string WsStatus
        {
            get => _wsStatus;
            set => SetProperty(ref _wsStatus, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string SelectedSymbol
        {
            get => _selectedSymbol;
            set
            {
                if (SetProperty(ref _selectedSymbol, value))
                {
                    OnSymbolChangedAsync(value);
                }
            }
        }

        public ObservableCollection<string> FilteredSymbols
        {
            get => _filteredSymbols;
            set => SetProperty(ref _filteredSymbols, value);
        }

        public TickerModel Ticker
        {
            get => _ticker;
            set => SetProperty(ref _ticker, value);
        }

        public string SpreadDisplay
        {
            get => _spreadDisplay;
            set => SetProperty(ref _spreadDisplay, value);
        }

        public ObservableCollection<OrderBookItem> Bids
        {
            get => _bids;
            set => SetProperty(ref _bids, value);
        }

        public ObservableCollection<OrderBookItem> Asks
        {
            get => _asks;
            set => SetProperty(ref _asks, value);
        }

        public ObservableCollection<TradeModel> RecentTrades
        {
            get => _recentTrades;
            set => SetProperty(ref _recentTrades, value);
        }

        public ObservableCollection<Candle> Candles
        {
            get => _candles;
            set => SetProperty(ref _candles, value);
        }

        public ObservableCollection<BalanceModel> Balances
        {
            get => _balances;
            set => SetProperty(ref _balances, value);
        }

        public ObservableCollection<PositionModel> Positions
        {
            get => _positions;
            set => SetProperty(ref _positions, value);
        }

        public ObservableCollection<OrderModel> OpenOrders
        {
            get => _openOrders;
            set => SetProperty(ref _openOrders, value);
        }

        public ObservableCollection<OrderModel> OrderHistory
        {
            get => _orderHistory;
            set => SetProperty(ref _orderHistory, value);
        }

        // Форма ордера
        public string OrderPrice
        {
            get => _orderPrice;
            set
            {
                if (SetProperty(ref _orderPrice, value))
                {
                    UpdateTotalCost();
                }
            }
        }

        public string OrderQuantity
        {
            get => _orderQuantity;
            set
            {
                if (SetProperty(ref _orderQuantity, value))
                {
                    UpdateTotalCost();
                }
            }
        }

        public string OrderStopLoss
        {
            get => _orderStopLoss;
            set => SetProperty(ref _orderStopLoss, value);
        }

        public string OrderTakeProfit
        {
            get => _orderTakeProfit;
            set => SetProperty(ref _orderTakeProfit, value);
        }

        public double TotalCost
        {
            get => _totalCost;
            set => SetProperty(ref _totalCost, value);
        }

        public bool IsLimitOrder
        {
            get => _isLimitOrder;
            set
            {
                if (SetProperty(ref _isLimitOrder, value))
                {
                    OnPropertyChanged(nameof(IsMarketOrder));
                    UpdateTotalCost();
                    PersistTradingPreferences();
                }
            }
        }

        public bool IsMarketOrder
        {
            get => !_isLimitOrder;
            set => IsLimitOrder = !value;
        }

        public bool QuantityInUsdt
        {
            get => _quantityInUsdt;
            set
            {
                if (SetProperty(ref _quantityInUsdt, value))
                {
                    OnPropertyChanged(nameof(QuantityInContracts));
                    OnPropertyChanged(nameof(QuantityLabel));
                    UpdateTotalCost();
                    PersistTradingPreferences();
                    ApplyDefaultOrderQuantity();
                }
            }
        }

        public bool QuantityInContracts
        {
            get => !_quantityInUsdt;
            set => QuantityInUsdt = !value;
        }

        public string QuantityLabel => QuantityInUsdt ? "Сумма USDT:" : "Количество:";

        public string ContractQtyPreview
        {
            get => _contractQtyPreview;
            set => SetProperty(ref _contractQtyPreview, value);
        }

        // API Диагностика — см. окно «Журнал»
        #endregion

        #region Команды
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenLogsCommand { get; }
        public ICommand PlaceBuyOrderCommand { get; }
        public ICommand PlaceSellOrderCommand { get; }
        public ICommand CancelOrderCommand { get; }
        public ICommand RefreshBalancesCommand { get; }
        public ICommand SetPercentageQtyCommand { get; }
        public ICommand SetPositionSlTpCommand { get; }
        #endregion

        public MainViewModel()
        {
            Action<string> uiLogger = message => AppServices.Log.Info(message);

            _apiService = new BinanceApiService(uiLogger);
            _wsService = new BinanceWebSocketService(uiLogger);

            _wsService.OnConnectionStatusChanged += status => WsStatus = status;
            _wsService.OnTickerReceived += OnWsTickerReceived;
            _wsService.OnDepthReceived += OnWsDepthReceived;
            _wsService.OnTradeReceived += OnWsTradeReceived;
            _wsService.OnKlineReceived += OnWsKlineReceived;

            ApplyCredentials(AppServices.Settings.ApiKey, AppServices.Settings.SecretKey);
            ApplyTradingPreferences(AppServices.Settings);

            OpenSettingsCommand = new RelayCommand(OpenSettings);
            OpenLogsCommand = new RelayCommand(OpenLogs);
            PlaceBuyOrderCommand = new RelayCommand(async () => await ExecuteOrderAsync("BUY"));
            PlaceSellOrderCommand = new RelayCommand(async () => await ExecuteOrderAsync("SELL"));
            CancelOrderCommand = new RelayCommand(async (param) => await ExecuteCancelOrderAsync(param));
            RefreshBalancesCommand = new RelayCommand(async () => await RefreshAccountDataAsync());
            SetPercentageQtyCommand = new RelayCommand((pct) => ExecutePercentageQty(pct));
            SetPositionSlTpCommand = new RelayCommand((param) => OpenPositionSlTp(param));

            AppServices.Log.Info("Hermes.BinanceDemoFuturesTerminal запущен.");
            _ = InitializeTerminalAsync();
        }

        public void ApplyCredentials(string apiKey, string secretKey)
        {
            _apiKey = apiKey ?? string.Empty;
            _secretKey = secretKey ?? string.Empty;
            _apiService.ApiKey = _apiKey;
            _apiService.SecretKey = _secretKey;

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                RunOnUIThread(() =>
                {
                    Balances.Clear();
                    OpenOrders.Clear();
                    OrderHistory.Clear();
                    Positions.Clear();
                });
                return;
            }

            _ = RefreshAccountDataAsync();
        }

        private void ApplyTradingPreferences(PlatformSettings settings)
        {
            _isLimitOrder = settings.IsLimitOrder;
            _quantityInUsdt = settings.QuantityInUsdt;
            OnPropertyChanged(nameof(IsLimitOrder));
            OnPropertyChanged(nameof(IsMarketOrder));
            OnPropertyChanged(nameof(QuantityInUsdt));
            OnPropertyChanged(nameof(QuantityInContracts));
            OnPropertyChanged(nameof(QuantityLabel));
            UpdateTotalCost();
        }

        private void PersistTradingPreferences()
        {
            var settings = AppServices.Settings;
            if (settings.IsLimitOrder == IsLimitOrder && settings.QuantityInUsdt == QuantityInUsdt)
            {
                return;
            }

            settings.IsLimitOrder = IsLimitOrder;
            settings.QuantityInUsdt = QuantityInUsdt;
            AppServices.SaveSettings(settings);
            AppServices.Log.Info(
                $"Торговые настройки сохранены: {(IsLimitOrder ? "LIMIT" : "MARKET")}, {(QuantityInUsdt ? "USDT" : "контракты")}.");
        }

        private void OpenSettings()
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(ApplyCredentials) { Owner = Application.Current.MainWindow };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        private void OpenLogs()
        {
            if (_logsWindow is { IsVisible: true })
            {
                _logsWindow.Activate();
                return;
            }

            _logsWindow = new LogsWindow { Owner = Application.Current.MainWindow };
            _logsWindow.Closed += (_, _) => _logsWindow = null;
            _logsWindow.Show();
        }

        #region Логика Инициализации
        private async Task InitializeTerminalAsync()
        {
            AddLog("Запуск USDT-M Futures Demo. Загрузка контрактов...");
            
            var symbols = await _apiService.GetExchangeInfoAsync();
            if (symbols == null || !symbols.Any())
            {
                AddLog("Ошибка: не удалось загрузить exchangeInfo с demo-fapi.binance.com.");
                return;
            }

            _allSymbols = symbols
                .Where(s => s.Status.Equals("TRADING", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(s.ContractType, "PERPETUAL", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(s.QuoteAsset, "USDT", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Symbol)
                .ToList();

            RunOnUIThread(() =>
            {
                ApplyFilter();
                
                // Выбор пары по умолчанию (BTCUSDT) или первой из списка
                var defaultSymbol = _allSymbols.FirstOrDefault(s => s.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase))?.Symbol 
                                   ?? _allSymbols.FirstOrDefault()?.Symbol;

                if (!string.IsNullOrEmpty(defaultSymbol))
                {
                    SelectedSymbol = defaultSymbol;
                }
            });

            // Запуск загрузки балансов, если ключи установлены
            if (!string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_secretKey))
            {
                await RefreshAccountDataAsync();
            }
        }
        #endregion

        #region Фильтрация и Переключение Символа
        private void ApplyFilter()
        {
            if (_allSymbols == null) return;

            var filtered = _allSymbols
                .Select(s => s.Symbol)
                .Where(s => string.IsNullOrEmpty(SearchText) || s.Contains(SearchText.ToUpper()))
                .ToList();

            FilteredSymbols.Clear();
            foreach (var s in filtered)
            {
                FilteredSymbols.Add(s);
            }
        }

        private async void OnSymbolChangedAsync(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return;

            AddLog($"Переключение на пару {symbol}");
            _applyMinQtyOnNextPrice = true;

            // Сброс старых стаканов и сделок
            Bids.Clear();
            Asks.Clear();
            RecentTrades.Clear();
            Candles.Clear();
            SpreadDisplay = "Спред: Расчет...";

            // 1. Загрузка исторических свечей (1m, 100 штук) через REST
            var historicalCandles = await _apiService.GetKlinesAsync(symbol, "1m", 100);
            var depthSnapshot = await _apiService.GetDepthAsync(symbol, 20);
            RunOnUIThread(() =>
            {
                foreach (var candle in historicalCandles)
                {
                    Candles.Add(candle);
                }

                if (depthSnapshot != null)
                {
                    ApplyDepth(depthSnapshot);
                }

                ApplyDefaultOrderQuantity();
            });

            // 2. Подписка на стримы в WebSockets
            await _wsService.SubscribeSymbolAsync(symbol);

            // 3. Обновление истории ордеров по этой паре
            if (!string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_secretKey))
            {
                await RefreshOrdersHistoryAsync(symbol);
            }
        }
        #endregion

        #region События WebSocket (С переводом на UI поток)
        private void OnWsTickerReceived(WsTickerPayload payload)
        {
            if (!payload.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase)) return;

            RunOnUIThread(() =>
            {
                Ticker = new TickerModel
                {
                    Symbol = payload.Symbol,
                    LastPrice = double.Parse(payload.LastPrice, CultureInfo.InvariantCulture),
                    PriceChangePercent = double.Parse(payload.PriceChangePercent, CultureInfo.InvariantCulture),
                    HighPrice = double.Parse(payload.HighPrice, CultureInfo.InvariantCulture),
                    LowPrice = double.Parse(payload.LowPrice, CultureInfo.InvariantCulture),
                    Volume = double.Parse(payload.Volume, CultureInfo.InvariantCulture)
                };

                // Если цена ордера пуста (при переключении пары), автозаполняем текущей ценой для лимитного ордера
                if (string.IsNullOrEmpty(OrderPrice) && IsLimitOrder)
                {
                    OrderPrice = payload.LastPrice;
                }

                if (_applyMinQtyOnNextPrice)
                {
                    ApplyDefaultOrderQuantity();
                    _applyMinQtyOnNextPrice = false;
                }
            });
        }

        private void OnWsDepthReceived(WsDepthPayload payload)
        {
            RunOnUIThread(() => ApplyDepth(payload));
        }

        private void ApplyDepth(WsDepthPayload payload)
        {
            var bidLevels = payload.GetBids();
            var askLevels = payload.GetAsks();
            if (bidLevels.Count == 0 && askLevels.Count == 0)
            {
                return;
            }

            var newBids = new List<OrderBookItem>();
            int bidCount = Math.Min(10, bidLevels.Count);
            for (int i = 0; i < bidCount; i++)
            {
                double price = double.Parse(bidLevels[i][0], CultureInfo.InvariantCulture);
                double amount = double.Parse(bidLevels[i][1], CultureInfo.InvariantCulture);
                if (amount <= 0)
                {
                    continue;
                }

                newBids.Add(new OrderBookItem { Price = price, Amount = amount, Total = price * amount });
            }

            double maxBidTotal = newBids.Any() ? newBids.Max(b => b.Total) : 1;
            foreach (var b in newBids) b.Percentage = (b.Total / maxBidTotal) * 100;

            Bids.Clear();
            foreach (var b in newBids) Bids.Add(b);

            var newAsks = new List<OrderBookItem>();
            int askCount = Math.Min(10, askLevels.Count);
            for (int i = 0; i < askCount; i++)
            {
                double price = double.Parse(askLevels[i][0], CultureInfo.InvariantCulture);
                double amount = double.Parse(askLevels[i][1], CultureInfo.InvariantCulture);
                if (amount <= 0)
                {
                    continue;
                }

                newAsks.Add(new OrderBookItem { Price = price, Amount = amount, Total = price * amount });
            }

            double maxAskTotal = newAsks.Any() ? newAsks.Max(a => a.Total) : 1;
            foreach (var a in newAsks) a.Percentage = (a.Total / maxAskTotal) * 100;

            Asks.Clear();
            var sortedAsks = newAsks.OrderByDescending(a => a.Price).ToList();
            foreach (var a in sortedAsks) Asks.Add(a);

            if (Bids.Any() && sortedAsks.Any())
            {
                double bestBid = Bids[0].Price;
                double bestAsk = sortedAsks[^1].Price;
                double spread = bestAsk - bestBid;
                double spreadPct = bestAsk > 0 ? (spread / bestAsk) * 100 : 0;
                SpreadDisplay = $"Спред: {spread:N2} ({spreadPct:F3}%)";
            }
        }

        private void OnWsTradeReceived(WsTradePayload payload)
        {
            if (!payload.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase)) return;

            RunOnUIThread(() =>
            {
                var trade = new TradeModel
                {
                    Time = DateTimeOffset.FromUnixTimeMilliseconds(payload.Time).DateTime.ToLocalTime(),
                    Price = double.Parse(payload.Price, CultureInfo.InvariantCulture),
                    Amount = double.Parse(payload.Qty, CultureInfo.InvariantCulture),
                    IsBuy = !payload.IsBuyerMaker // BuyerMaker true = продажа, false = покупка
                };

                RecentTrades.Insert(0, trade);
                if (RecentTrades.Count > 25)
                {
                    RecentTrades.RemoveAt(RecentTrades.Count - 1);
                }
            });
        }

        private void OnWsKlineReceived(WsKlinePayload payload)
        {
            if (!payload.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase)) return;

            RunOnUIThread(() =>
            {
                var data = payload.KlineData;
                var candle = new Candle
                {
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(data.OpenTime).DateTime.ToLocalTime(),
                    CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(data.CloseTime).DateTime.ToLocalTime(),
                    Open = double.Parse(data.Open, CultureInfo.InvariantCulture),
                    High = double.Parse(data.High, CultureInfo.InvariantCulture),
                    Low = double.Parse(data.Low, CultureInfo.InvariantCulture),
                    Close = double.Parse(data.Close, CultureInfo.InvariantCulture),
                    Volume = double.Parse(data.Volume, CultureInfo.InvariantCulture)
                };

                if (Candles.Any())
                {
                    var lastCandle = Candles[Candles.Count - 1];
                    if (lastCandle.OpenTime == candle.OpenTime)
                    {
                        // Заменяем текущую свечу обновленными данными
                        Candles[Candles.Count - 1] = candle;
                    }
                    else if (candle.OpenTime > lastCandle.OpenTime)
                    {
                        // Это новая свеча, добавляем её
                        Candles.Add(candle);
                        if (Candles.Count > 120)
                        {
                            Candles.RemoveAt(0);
                        }
                    }
                }
                else
                {
                    Candles.Add(candle);
                }
            });
        }
        #endregion

        #region Работа с Ордерами и Балансами (REST)
        private async Task RefreshAccountDataAsync()
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey)) return;

            AddLog("Запрос данных аккаунта и открытых ордеров...");
            
            // Балансы
            var balances = await _apiService.GetBalancesAsync();
            var openOrders = await _apiService.GetOpenOrdersAsync();
            var positions = await _apiService.GetPositionsAsync();
            PositionProtectionHelper.ApplyProtectionFromOrders(positions, openOrders);

            RunOnUIThread(() =>
            {
                Balances.Clear();
                foreach (var b in balances) Balances.Add(b);

                Positions.Clear();
                foreach (var p in positions) Positions.Add(p);

                OpenOrders.Clear();
                foreach (var o in openOrders)
                {
                    OpenOrders.Add(MapBinanceOrderToModel(o));
                }
            });

            // История ордеров для текущей выбранной пары
            if (!string.IsNullOrEmpty(SelectedSymbol))
            {
                await RefreshOrdersHistoryAsync(SelectedSymbol);
            }
        }

        private async Task RefreshOrdersHistoryAsync(string symbol)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey) || string.IsNullOrEmpty(symbol)) return;

            var history = await _apiService.GetOrderHistoryAsync(symbol);
            RunOnUIThread(() =>
            {
                OrderHistory.Clear();
                // Сортируем историю ордеров по времени: самые свежие сверху
                var sorted = history.OrderByDescending(o => o.Time > 0 ? o.Time : o.UpdateTime).ToList();
                foreach (var o in sorted)
                {
                    OrderHistory.Add(MapBinanceOrderToModel(o));
                }
            });
        }

        // Размещение ордера BUY/SELL
        private async Task ExecuteOrderAsync(string side)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                MessageBox.Show("Для отправки ордеров откройте «Настройки» и сохраните API-ключи Demo.", "Ошибка авторизации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(SelectedSymbol)) return;

            if (!TryResolveContractQuantity(out var qty, out var qtyText, out var error))
            {
                MessageBox.Show(error, "Неверный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? priceText = null;
            if (IsLimitOrder)
            {
                if (!double.TryParse(OrderPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double pr) || pr <= 0)
                {
                    MessageBox.Show("Введите корректную цену для лимитного ордера.", "Неверный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var symbolInfo = GetSelectedSymbolInfo();
                priceText = symbolInfo?.FormatPrice(pr) ?? pr.ToString(CultureInfo.InvariantCulture);
            }

            var priceForRisk = priceText != null
                ? double.Parse(priceText, CultureInfo.InvariantCulture)
                : GetOrderPriceEstimate();
            var notional = RiskManager.EstimateNotionalUsdt(qty, priceForRisk);
            var positionsList = Positions.ToList();
            var isNew = !positionsList.Any(p =>
                p.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase));
            var riskError = RiskManager.ValidateOrder(
                AppServices.Settings, notional, positionsList, SelectedSymbol, isNew);
            if (riskError != null)
            {
                AppServices.Log.Warn(riskError);
                MessageBox.Show(riskError, "Риск-менеджер", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var isLong = side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
            if (!TryBuildProtectionPlan(isLong, priceForRisk, out var stopLossPrice, out var takeProfitPrice, out error))
            {
                MessageBox.Show(error, "SL / TP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string orderType = IsLimitOrder ? "LIMIT" : "MARKET";
            var usdtNote = QuantityInUsdt ? $" ({OrderQuantity} USDT → {qtyText} контр.)" : string.Empty;
            var protectionNote = BuildProtectionLogNote(stopLossPrice, takeProfitPrice);
            AddLog($"Отправка ордера: {side} {orderType} {qtyText} {SelectedSymbol}{usdtNote} @ {(priceText ?? "MARKET")}{protectionNote}");

            try
            {
                var response = await _apiService.PlaceOrderAsync(SelectedSymbol, side, orderType, qtyText, priceText);
                if (response != null)
                {
                    var protectionErrors = await ApplyProtectionOrdersAsync(
                        SelectedSymbol,
                        isLong,
                        qtyText,
                        stopLossPrice,
                        takeProfitPrice);

                    var message = $"Ордер успешно размещен!\nID: {response.OrderId}\nСтатус: {response.Status}";
                    if (protectionErrors.Count > 0)
                    {
                        message += "\n\nSL/TP:\n" + string.Join("\n", protectionErrors);
                    }

                    MessageBox.Show(message, "Успех", MessageBoxButton.OK,
                        protectionErrors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    ApplyDefaultOrderQuantity();
                    await RefreshAccountDataAsync();
                }
            }
            catch (Exception ex)
            {
                AppServices.Log.Error($"PlaceOrder: {ex.Message}");
                MessageBox.Show($"Ошибка размещения ордера:\n{ex.Message}", "Ошибка API", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Отмена открытого ордера
        private async Task ExecuteCancelOrderAsync(object param)
        {
            if (param is not OrderModel order) return;

            var result = MessageBox.Show($"Вы уверены, что хотите отменить ордер ID {order.OrderId} ({order.Symbol})?", 
                "Подтверждение отмены", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.No) return;

            AddLog($"Отмена ордера ID {order.OrderId}...");
            var response = await _apiService.CancelOrderAsync(order.Symbol, order.OrderId);
            if (response != null)
            {
                MessageBox.Show($"Ордер {order.OrderId} отменен.", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshAccountDataAsync();
            }
            else
            {
                MessageBox.Show("Не удалось отменить ордер. Возможно, он уже исполнен или отменен.", "Ошибка отмены", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExecutePercentageQty(object pctParam)
        {
            if (pctParam == null) return;

            string pctStr = pctParam.ToString()!.TrimEnd('%');
            if (!double.TryParse(pctStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double percent)) return;

            percent /= 100.0;

            var currentSymbolInfo = _allSymbols.FirstOrDefault(s => s.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase));
            if (currentSymbolInfo == null) return;

            var quoteBalance = Balances.FirstOrDefault(b => b.Asset.Equals(currentSymbolInfo.QuoteAsset, StringComparison.OrdinalIgnoreCase));
            double availableQuote = quoteBalance?.Free ?? 0.0;

            if (QuantityInUsdt)
            {
                OrderQuantity = (availableQuote * percent).ToString("F2", CultureInfo.InvariantCulture);
                return;
            }

            if (Ticker == null) return;

            double price = GetOrderPriceEstimate();
            if (price <= 0) return;

            double targetQty = currentSymbolInfo.RoundQuantity((availableQuote * percent) / price);
            OrderQuantity = currentSymbolInfo.FormatQuantity(targetQty);
        }
        #endregion

        #region Вспомогательные методы
        private OrderModel MapBinanceOrderToModel(BinanceOrder raw)
        {
            long orderTime = raw.Time > 0 ? raw.Time : raw.UpdateTime;
            
            return new OrderModel
            {
                OrderId = raw.OrderId,
                Symbol = raw.Symbol,
                Time = DateTimeOffset.FromUnixTimeMilliseconds(orderTime).DateTime.ToLocalTime(),
                Side = raw.Side,
                Type = raw.Type,
                Price = double.Parse(raw.Price, CultureInfo.InvariantCulture),
                OrigQty = double.Parse(raw.OrigQty, CultureInfo.InvariantCulture),
                ExecutedQty = double.Parse(raw.ExecutedQty, CultureInfo.InvariantCulture),
                Status = raw.Status
            };
        }

        private void UpdateTotalCost()
        {
            if (!double.TryParse(OrderQuantity.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double input) || input <= 0)
            {
                TotalCost = 0.0;
                ContractQtyPreview = string.Empty;
                return;
            }

            var price = GetOrderPriceEstimate();
            var symbolInfo = GetSelectedSymbolInfo();

            if (QuantityInUsdt)
            {
                TotalCost = input;
                if (price > 0 && symbolInfo != null)
                {
                    var contracts = symbolInfo.RoundQuantity(input / price);
                    ContractQtyPreview = contracts > 0
                        ? $"≈ {symbolInfo.FormatQuantity(contracts)} {symbolInfo.BaseAsset}"
                        : string.Empty;
                }
                else
                {
                    ContractQtyPreview = string.Empty;
                }

                return;
            }

            TotalCost = price > 0 ? input * price : 0.0;
            ContractQtyPreview = string.Empty;
        }

        private double GetOrderPriceEstimate()
        {
            if (IsLimitOrder
                && double.TryParse(OrderPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double limitPrice)
                && limitPrice > 0)
            {
                return limitPrice;
            }

            if (Ticker != null && Ticker.LastPrice > 0)
            {
                return Ticker.LastPrice;
            }

            if (Bids.Any() && Asks.Any())
            {
                return (Bids[0].Price + Asks[0].Price) / 2.0;
            }

            return 0;
        }

        private SymbolInfo? GetSelectedSymbolInfo() =>
            _allSymbols.FirstOrDefault(s => s.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase));

        private bool TryResolveContractQuantity(out double qty, out string qtyText, out string error)
        {
            qty = 0;
            qtyText = string.Empty;
            error = string.Empty;

            if (!double.TryParse(OrderQuantity.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var input) || input <= 0)
            {
                error = QuantityInUsdt
                    ? "Введите корректную сумму в USDT."
                    : "Введите корректное количество контрактов.";
                return false;
            }

            var symbolInfo = GetSelectedSymbolInfo();
            if (symbolInfo == null)
            {
                error = "Символ не выбран.";
                return false;
            }

            if (QuantityInUsdt)
            {
                var price = GetOrderPriceEstimate();
                if (price <= 0)
                {
                    error = "Нет цены для расчёта количества. Дождитесь тикера или укажите цену.";
                    return false;
                }

                qty = symbolInfo.RoundQuantity(input / price);
            }
            else
            {
                qty = symbolInfo.RoundQuantity(input);
            }

            if (qty <= 0)
            {
                error = QuantityInUsdt
                    ? "Сумма USDT слишком мала для минимального лота."
                    : "Количество меньше минимального шага лота.";
                return false;
            }

            var minQty = symbolInfo.GetMinQty();
            if (qty < minQty)
            {
                error = $"Минимальное количество для {symbolInfo.Symbol}: {symbolInfo.FormatQuantity(minQty)}.";
                return false;
            }

            qtyText = symbolInfo.FormatQuantity(qty);
            return true;
        }

        private void AddLog(string message) => AppServices.Log.Info(message);

        private void ApplyDefaultOrderQuantity()
        {
            var symbolInfo = GetSelectedSymbolInfo();
            if (symbolInfo == null)
            {
                return;
            }

            var price = GetOrderPriceEstimate();
            var defaultInput = symbolInfo.GetDefaultQuantityInput(QuantityInUsdt, price);
            if (string.IsNullOrEmpty(defaultInput))
            {
                return;
            }

            OrderQuantity = defaultInput;
        }

        private void OpenPositionSlTp(object? param)
        {
            if (param is not PositionModel position)
            {
                return;
            }

            var window = new PositionSlTpWindow(position, ApplyPositionProtectionAsync)
            {
                Owner = Application.Current.MainWindow,
            };
            window.ShowDialog();
        }

        private async Task ApplyPositionProtectionAsync(PositionModel position, string stopLossText, string takeProfitText)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                throw new InvalidOperationException("Сначала сохраните API-ключи в настройках.");
            }

            var symbolInfo = _allSymbols.FirstOrDefault(s =>
                s.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase));
            if (symbolInfo == null)
            {
                throw new InvalidOperationException("Не найдена информация о символе.");
            }

            var qtyText = symbolInfo.FormatQuantity(Math.Abs(position.Size));
            if (!TryBuildProtectionPlan(symbolInfo, position.IsLong, position.MarkPrice, stopLossText, takeProfitText,
                    out var stopLossPrice, out var takeProfitPrice, out var error))
            {
                throw new InvalidOperationException(error);
            }

            var errors = await ApplyProtectionOrdersAsync(
                position.Symbol,
                position.IsLong,
                qtyText,
                stopLossPrice,
                takeProfitPrice,
                replaceExisting: true);

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("\n", errors));
            }

            AppServices.Log.Info($"SL/TP обновлены для {position.Symbol}.");
            await RefreshAccountDataAsync();
        }

        private bool TryBuildProtectionPlan(
            bool isLong,
            double referencePrice,
            out double? stopLossPrice,
            out double? takeProfitPrice,
            out string error)
        {
            var symbolInfo = GetSelectedSymbolInfo();
            if (symbolInfo == null)
            {
                stopLossPrice = null;
                takeProfitPrice = null;
                error = "Символ не выбран.";
                return false;
            }

            return TryBuildProtectionPlan(
                symbolInfo,
                isLong,
                referencePrice,
                OrderStopLoss,
                OrderTakeProfit,
                out stopLossPrice,
                out takeProfitPrice,
                out error);
        }

        private static bool TryBuildProtectionPlan(
            SymbolInfo symbolInfo,
            bool isLong,
            double referencePrice,
            string stopLossText,
            string takeProfitText,
            out double? stopLossPrice,
            out double? takeProfitPrice,
            out string error)
        {
            stopLossPrice = null;
            takeProfitPrice = null;
            error = string.Empty;

            if (PositionProtectionHelper.TryParseOptionalPrice(stopLossText, out var slRaw))
            {
                stopLossPrice = double.Parse(symbolInfo.FormatPrice(slRaw), CultureInfo.InvariantCulture);
            }

            if (PositionProtectionHelper.TryParseOptionalPrice(takeProfitText, out var tpRaw))
            {
                takeProfitPrice = double.Parse(symbolInfo.FormatPrice(tpRaw), CultureInfo.InvariantCulture);
            }

            if (!stopLossPrice.HasValue && !takeProfitPrice.HasValue)
            {
                return true;
            }

            return PositionProtectionHelper.ValidateProtection(
                isLong,
                referencePrice,
                stopLossPrice,
                takeProfitPrice,
                out error);
        }

        private async Task<List<string>> ApplyProtectionOrdersAsync(
            string symbol,
            bool isLong,
            string quantityText,
            double? stopLossPrice,
            double? takeProfitPrice,
            bool replaceExisting = false)
        {
            var errors = new List<string>();
            if (!stopLossPrice.HasValue && !takeProfitPrice.HasValue)
            {
                return errors;
            }

            var closeSide = isLong ? "SELL" : "BUY";
            var symbolInfo = _allSymbols.FirstOrDefault(s =>
                s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            if (symbolInfo == null)
            {
                errors.Add("Не удалось определить параметры символа для SL/TP.");
                return errors;
            }

            try
            {
                if (replaceExisting || stopLossPrice.HasValue)
                {
                    await _apiService.CancelConditionalOrdersAsync(symbol, PositionProtectionHelper.IsStopLossType);
                }

                if (replaceExisting || takeProfitPrice.HasValue)
                {
                    await _apiService.CancelConditionalOrdersAsync(symbol, PositionProtectionHelper.IsTakeProfitType);
                }

                if (stopLossPrice.HasValue)
                {
                    var stopText = symbolInfo.FormatPrice(stopLossPrice.Value);
                    await _apiService.PlaceConditionalOrderAsync(
                        symbol, closeSide, "STOP_MARKET", quantityText, stopText);
                    AppServices.Log.Info($"Stop-Loss установлен: {symbol} @ {stopText}");
                }

                if (takeProfitPrice.HasValue)
                {
                    var takeText = symbolInfo.FormatPrice(takeProfitPrice.Value);
                    await _apiService.PlaceConditionalOrderAsync(
                        symbol, closeSide, "TAKE_PROFIT_MARKET", quantityText, takeText);
                    AppServices.Log.Info($"Take-Profit установлен: {symbol} @ {takeText}");
                }
            }
            catch (Exception ex)
            {
                AppServices.Log.Error($"SL/TP: {ex.Message}");
                errors.Add(ex.Message);
            }

            return errors;
        }

        private static string BuildProtectionLogNote(double? stopLossPrice, double? takeProfitPrice)
        {
            if (!stopLossPrice.HasValue && !takeProfitPrice.HasValue)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (stopLossPrice.HasValue)
            {
                parts.Add($"SL={stopLossPrice.Value.ToString(CultureInfo.InvariantCulture)}");
            }

            if (takeProfitPrice.HasValue)
            {
                parts.Add($"TP={takeProfitPrice.Value.ToString(CultureInfo.InvariantCulture)}");
            }

            return " | " + string.Join(", ", parts);
        }

        private void RunOnUIThread(Action action)
        {
            if (Application.Current != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(action);
                }
            }
            else
            {
                action();
            }
        }
        #endregion
    }
}
