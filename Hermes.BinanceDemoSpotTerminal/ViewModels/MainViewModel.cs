using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Hermes.BinanceDemoSpotTerminal.Helpers;
using Hermes.BinanceDemoSpotTerminal.Models;
using Hermes.BinanceDemoSpotTerminal.MVVM;
using Hermes.BinanceDemoSpotTerminal.Services;

namespace Hermes.BinanceDemoSpotTerminal.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly BinanceApiService _apiService;
        private readonly BinanceWebSocketService _wsService;

        // Поля ввода учетных данных API
        private string _apiKey = string.Empty;
        private string _secretKey = string.Empty;
        private string _wsStatus = "Отключено";

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
        private ObservableCollection<OrderModel> _openOrders = new ObservableCollection<OrderModel>();
        private ObservableCollection<OrderModel> _orderHistory = new ObservableCollection<OrderModel>();

        // Форма ордера
        private string _orderPrice = string.Empty;
        private string _orderQuantity = string.Empty;
        private double _totalCost = 0.0;
        private bool _isLimitOrder = true; // true = LIMIT, false = MARKET

        // API Диагностика
        private ObservableCollection<string> _apiLogs = new ObservableCollection<string>();

        #region Свойства
        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (SetProperty(ref _apiKey, value))
                {
                    _apiService.ApiKey = value;
                }
            }
        }

        public string SecretKey
        {
            get => _secretKey;
            set
            {
                if (SetProperty(ref _secretKey, value))
                {
                    _apiService.SecretKey = value;
                }
            }
        }

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
                }
            }
        }

        public bool IsMarketOrder
        {
            get => !_isLimitOrder;
            set => IsLimitOrder = !value;
        }

        // Логи диагностики
        public ObservableCollection<string> ApiLogs
        {
            get => _apiLogs;
            set => SetProperty(ref _apiLogs, value);
        }
        #endregion

        #region Команды
        public ICommand SaveCredentialsCommand { get; }
        public ICommand ClearCredentialsCommand { get; }
        public ICommand PlaceBuyOrderCommand { get; }
        public ICommand PlaceSellOrderCommand { get; }
        public ICommand CancelOrderCommand { get; }
        public ICommand RefreshBalancesCommand { get; }
        public ICommand SetPercentageQtyCommand { get; }
        #endregion

        public MainViewModel()
        {
            // Настройка логгера для логирования в UI
            Action<string> uiLogger = message => AddLog(message);

            _apiService = new BinanceApiService(uiLogger);
            _wsService = new BinanceWebSocketService(uiLogger);

            // Регистрация событий WebSocket
            _wsService.OnConnectionStatusChanged += status => WsStatus = status;
            _wsService.OnTickerReceived += OnWsTickerReceived;
            _wsService.OnDepthReceived += OnWsDepthReceived;
            _wsService.OnTradeReceived += OnWsTradeReceived;
            _wsService.OnKlineReceived += OnWsKlineReceived;

            // Загрузка сохраненных ключей
            var savedCreds = ConfigManager.LoadCredentials();
            ApiKey = savedCreds.ApiKey;
            SecretKey = savedCreds.SecretKey;

            // Инициализация команд
            SaveCredentialsCommand = new RelayCommand(SaveCredentials);
            ClearCredentialsCommand = new RelayCommand(ClearCredentials);
            PlaceBuyOrderCommand = new RelayCommand(async () => await ExecuteOrderAsync("BUY"));
            PlaceSellOrderCommand = new RelayCommand(async () => await ExecuteOrderAsync("SELL"));
            CancelOrderCommand = new RelayCommand(async (param) => await ExecuteCancelOrderAsync(param));
            RefreshBalancesCommand = new RelayCommand(async () => await RefreshAccountDataAsync());
            SetPercentageQtyCommand = new RelayCommand((pct) => ExecutePercentageQty(pct));

            // Запуск инициализации списка пар
            _ = InitializeTerminalAsync();
        }

        #region Логика Инициализации
        private async Task InitializeTerminalAsync()
        {
            AddLog("Запуск терминала. Загрузка спецификаций биржи...");
            
            // Получаем пары
            var symbols = await _apiService.GetExchangeInfoAsync();
            if (symbols == null || !symbols.Any())
            {
                AddLog("Ошибка: Не удалось загрузить спецификации биржи с Demo API. Проверьте интернет-соединение.");
                return;
            }

            // Фильтруем только активные пары (TRADING)
            _allSymbols = symbols.Where(s => s.Status.Equals("TRADING", StringComparison.OrdinalIgnoreCase))
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
            if (!string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(SecretKey))
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

            // Сброс старых стаканов и сделок
            Bids.Clear();
            Asks.Clear();
            RecentTrades.Clear();
            Candles.Clear();
            SpreadDisplay = "Спред: Расчет...";

            // 1. Загрузка исторических свечей (1m, 100 штук) через REST
            var historicalCandles = await _apiService.GetKlinesAsync(symbol, "1m", 100);
            RunOnUIThread(() =>
            {
                foreach (var candle in historicalCandles)
                {
                    Candles.Add(candle);
                }
            });

            // 2. Подписка на стримы в WebSockets
            await _wsService.SubscribeSymbolAsync(symbol);

            // 3. Обновление истории ордеров по этой паре
            if (!string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(SecretKey))
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
            });
        }

        private void OnWsDepthReceived(WsDepthPayload payload)
        {
            RunOnUIThread(() =>
            {
                // Заполнение Bids (Покупки)
                var newBids = new List<OrderBookItem>();
                int bidCount = Math.Min(10, payload.Bids.Count);
                for (int i = 0; i < bidCount; i++)
                {
                    double price = double.Parse(payload.Bids[i][0], CultureInfo.InvariantCulture);
                    double amount = double.Parse(payload.Bids[i][1], CultureInfo.InvariantCulture);
                    newBids.Add(new OrderBookItem { Price = price, Amount = amount, Total = price * amount });
                }

                double maxBidTotal = newBids.Any() ? newBids.Max(b => b.Total) : 1;
                foreach (var b in newBids) b.Percentage = (b.Total / maxBidTotal) * 100;

                Bids.Clear();
                foreach (var b in newBids) Bids.Add(b);

                // Заполнение Asks (Продажи)
                var newAsks = new List<OrderBookItem>();
                int askCount = Math.Min(10, payload.Asks.Count);
                for (int i = 0; i < askCount; i++)
                {
                    double price = double.Parse(payload.Asks[i][0], CultureInfo.InvariantCulture);
                    double amount = double.Parse(payload.Asks[i][1], CultureInfo.InvariantCulture);
                    newAsks.Add(new OrderBookItem { Price = price, Amount = amount, Total = price * amount });
                }

                double maxAskTotal = newAsks.Any() ? newAsks.Max(a => a.Total) : 1;
                foreach (var a in newAsks) a.Percentage = (a.Total / maxAskTotal) * 100;

                Asks.Clear();
                // Для эстетики: Аски сортируем по убыванию (чтобы самые дорогие были вверху стакана)
                var sortedAsks = newAsks.OrderByDescending(a => a.Price).ToList();
                foreach (var a in sortedAsks) Asks.Add(a);

                // Расчет спреда
                if (Bids.Any() && Asks.Any())
                {
                    double bestBid = Bids[0].Price;
                    double bestAsk = sortedAsks[sortedAsks.Count - 1].Price;
                    double spread = bestAsk - bestBid;
                    double spreadPct = (spread / bestAsk) * 100;
                    SpreadDisplay = $"Спред: {spread.ToString("N4")} ({spreadPct.ToString("F3")}%)";
                }
            });
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

        #region Учетные данные API
        private void SaveCredentials()
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
            {
                MessageBox.Show("Пожалуйста, заполните оба поля API-ключа и Секретного ключа.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ConfigManager.SaveCredentials(ApiKey, SecretKey);
            AddLog("Учетные данные API сохранены в %LocalAppData%\\HermesBinanceDemoSpot\\api_credentials.json.");
            MessageBox.Show("Ключи успешно сохранены!", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);

            // Инициируем обновление аккаунта
            _ = RefreshAccountDataAsync();
        }

        private void ClearCredentials()
        {
            ApiKey = string.Empty;
            SecretKey = string.Empty;
            ConfigManager.SaveCredentials(string.Empty, string.Empty);
            
            RunOnUIThread(() =>
            {
                Balances.Clear();
                OpenOrders.Clear();
                OrderHistory.Clear();
            });

            AddLog("Учетные данные API стерты.");
            MessageBox.Show("Сохраненные ключи удалены.", "Очищено", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        #endregion

        #region Работа с Ордерами и Балансами (REST)
        private async Task RefreshAccountDataAsync()
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey)) return;

            AddLog("Запрос данных аккаунта и открытых ордеров...");
            
            // Балансы
            var balances = await _apiService.GetBalancesAsync();
            
            // Открытые ордера
            var openOrders = await _apiService.GetOpenOrdersAsync();

            RunOnUIThread(() =>
            {
                // Балансы
                Balances.Clear();
                foreach (var b in balances) Balances.Add(b);

                // Открытые ордера
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
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey) || string.IsNullOrEmpty(symbol)) return;

            var history = await _apiService.GetOrderHistoryAsync(symbol);
            RunOnUIThread(() =>
            {
                OrderHistory.Clear();
                // Сортируем историю ордеров по времени: самые свежие сверху
                var sorted = history.OrderByDescending(o => o.Time > 0 ? o.Time : o.TransactTime).ToList();
                foreach (var o in sorted)
                {
                    OrderHistory.Add(MapBinanceOrderToModel(o));
                }
            });
        }

        // Размещение ордера BUY/SELL
        private async Task ExecuteOrderAsync(string side)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
            {
                MessageBox.Show("Для отправки ордеров введите и сохраните API-ключи.", "Ошибка авторизации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(SelectedSymbol)) return;

            // Парсинг количества
            if (!double.TryParse(OrderQuantity.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double qty) || qty <= 0)
            {
                MessageBox.Show("Введите корректное количество.", "Неверный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double? priceValue = null;
            if (IsLimitOrder)
            {
                if (!double.TryParse(OrderPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double pr) || pr <= 0)
                {
                    MessageBox.Show("Введите корректную цену для лимитного ордера.", "Неверный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                priceValue = pr;
            }

            string orderType = IsLimitOrder ? "LIMIT" : "MARKET";
            AddLog($"Отправка ордера: {side} {orderType} {qty} {SelectedSymbol} @ {(priceValue.HasValue ? priceValue.Value.ToString() : "MARKET")}");

            try
            {
                var response = await _apiService.PlaceOrderAsync(SelectedSymbol, side, orderType, qty, priceValue);
                if (response != null)
                {
                    MessageBox.Show($"Ордер успешно размещен!\nID: {response.OrderId}\nСтатус: {response.Status}", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Обнуление формы ввода
                    OrderQuantity = string.Empty;
                    
                    // Запуск обновления балансов и ордеров
                    await RefreshAccountDataAsync();
                }
            }
            catch (Exception ex)
            {
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

        // Расчет заполнения формы по процентам
        private void ExecutePercentageQty(object pctParam)
        {
            if (pctParam == null || Ticker == null) return;
            
            string pctStr = pctParam.ToString().TrimEnd('%');
            if (!double.TryParse(pctStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double percent)) return;

            percent /= 100.0;

            // Находим баланс котируемой валюты (USDT) для BUY или базовой (BTC) для SELL
            string targetAsset = string.Empty;
            
            // Получаем спецификации пары
            var currentSymbolInfo = _allSymbols.FirstOrDefault(s => s.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase));
            if (currentSymbolInfo == null) return;

            // Предположим, мы делаем Покупку (нам нужен QuoteAsset, например USDT)
            // Или Продажу (нам нужен BaseAsset, например BTC)
            // Но в нашей форме кнопки 25/50/75/100 обычно привязаны к активному направлению (например Покупка).
            // Чтобы не усложнять, мы сделаем универсальный расчет:
            // Если мы рассчитываем покупку: кол-во = (QuoteBalance * percent) / Price
            // Если мы рассчитываем продажу: кол-во = BaseBalance * percent

            double price = Ticker.LastPrice;
            if (IsLimitOrder && double.TryParse(OrderPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double limitPrice) && limitPrice > 0)
            {
                price = limitPrice;
            }

            // Покупка: ищем котируемую валюту (например, USDT)
            var quoteBalance = Balances.FirstOrDefault(b => b.Asset.Equals(currentSymbolInfo.QuoteAsset, StringComparison.OrdinalIgnoreCase));
            double availableQuote = quoteBalance?.Free ?? 0.0;

            // Продажа: ищем базовую валюту (например, BTC)
            var baseBalance = Balances.FirstOrDefault(b => b.Asset.Equals(currentSymbolInfo.BaseAsset, StringComparison.OrdinalIgnoreCase));
            double availableBase = baseBalance?.Free ?? 0.0;

            // Вычисляем значение (мы можем оценить BUY форму по наличию quote, либо просто вывести оба варианта)
            // Давайте посчитаем исходя из текущего заполнения цены:
            // Для удобства, если мы хотим ПОКУПАТЬ, используем баланс quoteAsset
            // Если мы хотим ПРОДАВАТЬ, используем баланс baseAsset.
            // Определим, какое действие планирует пользователь, на основании фокуса UI или просто предоставим расчет по покупке.
            // Чтобы дать пользователю максимальное удобство:
            // Давайте заполним кол-во так: если в форме введены данные, пользователь может использовать доступный баланс.
            // Сделаем более интеллектуально:
            // У нас на форме будет одна общая форма ордера. Если баланс базового актива больше нуля, мы можем продать его.
            // Но лучшая механика: рассчитать покупку на весь USDT: Qty = (USDT * percent) / Price.
            // Мы выведем диалог или просто по умолчанию посчитаем на покупку.
            // А лучше: если кликают 100% — давайте посчитаем BUY кол-во на 100% USDT, и выведем в поле. А для продажи покажем баланс.
            // Давайте сделаем так:
            // Покупка: Qty = (QuoteAsset_Balance * percent) / Price
            // Если пользователь хочет продать: Qty = BaseAsset_Balance * percent.
            // Мы можем посмотреть, какая кнопка нажата. Сделаем два метода или передадим параметр "BUY"/"SELL".
            // Отличный вариант: добавим на форму две разные вкладки (Купить / Продать) и будем переключать ActiveOrderSide.
            // Если мы хотим купить:
            double targetQty = (availableQuote * percent) / price;
            // Округляем по правилам точности пары
            int precision = currentSymbolInfo.BaseAssetPrecision;
            targetQty = Math.Floor(targetQty * Math.Pow(10, precision)) / Math.Pow(10, precision);
            
            OrderQuantity = targetQty.ToString(CultureInfo.InvariantCulture);
        }
        #endregion

        #region Вспомогательные методы
        private OrderModel MapBinanceOrderToModel(BinanceOrder raw)
        {
            long orderTime = raw.Time > 0 ? raw.Time : (raw.TransactTime > 0 ? raw.TransactTime : raw.UpdateTime);
            
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
            if (double.TryParse(OrderQuantity.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double qty) && qty > 0)
            {
                double price = 0;
                if (IsLimitOrder)
                {
                    double.TryParse(OrderPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out price);
                }
                else if (Ticker != null)
                {
                    price = Ticker.LastPrice;
                }

                TotalCost = qty * price;
            }
            else
            {
                TotalCost = 0.0;
            }
        }

        private void AddLog(string message)
        {
            RunOnUIThread(() =>
            {
                ApiLogs.Insert(0, message);
                if (ApiLogs.Count > 150)
                {
                    ApiLogs.RemoveAt(ApiLogs.Count - 1);
                }
            });
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
