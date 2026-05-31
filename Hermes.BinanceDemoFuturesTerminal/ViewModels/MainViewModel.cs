using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    public partial class MainViewModel : ObservableObject
    {
        private readonly BinanceApiService _apiService;
        private readonly BinanceWebSocketService _wsService;

        // API (из настроек)
        private string _apiKey = string.Empty;
        private string _secretKey = string.Empty;
        private string _wsStatus = "Отключено";
        private LogsWindow? _logsWindow;
        private SettingsWindow? _settingsWindow;
        private AdjustLeverageWindow? _adjustLeverageWindow;
        private AdjustMarginModeWindow? _adjustMarginModeWindow;

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
        private ObservableCollection<UserTradeModel> _tradeHistory = new ObservableCollection<UserTradeModel>();
        private readonly List<UserTradeModel> _tradeHistoryCache = [];
        private readonly List<UserTradeModel> _tradeStatsSource = [];
        private readonly HashSet<string> _knownTradedSymbols = new(StringComparer.OrdinalIgnoreCase);
        private readonly TradeStatsLoader _tradeStatsLoader;
        private bool _tradeStatsLoading;
        private int _tradeStatsFillCount;
        private bool _hideOtherTradeTickers = true;
        private int _tradeHistoryDays = 7;
        private string _tradeHistorySideFilter = "All";

        // Форма ордера
        private string _orderPrice = string.Empty;
        private string _orderQuantity = string.Empty;
        private string _orderStopLoss = string.Empty;
        private string _orderTakeProfit = string.Empty;
        private bool _useOrderSlTp;
        private double _totalCost = 0.0;
        private OrderEntryMode _orderEntryMode = OrderEntryMode.Limit;
        private bool _conditionalUseLimit = true;
        private string _orderStopPrice = string.Empty;
        private StopWorkingType _stopWorkingType = StopWorkingType.ContractPrice;
        private string _orderTimeInForce = "GTC";
        private bool _orderReduceOnly;
        private bool _isConditionalInfoOpen;
        private QuantityInputMode _quantityInputMode = QuantityInputMode.UsdtOrderSize;
        private bool _isUnitSelectorOpen;
        private int _exchangeSymbolLeverage = 20;
        private FuturesMarginType _symbolMarginMode = FuturesMarginType.Cross;
        private string _contractQtyPreview = string.Empty;
        private ChartIntervalOption _selectedChartInterval = ChartIntervalOption.All[0];
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

        public bool UseOrderSlTp
        {
            get => _useOrderSlTp;
            set => SetProperty(ref _useOrderSlTp, value);
        }

        public string OrderStopPrice
        {
            get => _orderStopPrice;
            set
            {
                if (SetProperty(ref _orderStopPrice, value))
                {
                    UpdateTotalCost();
                }
            }
        }

        public StopWorkingType SelectedStopWorkingType
        {
            get => _stopWorkingType;
            set
            {
                if (SetProperty(ref _stopWorkingType, value))
                {
                    OnPropertyChanged(nameof(StopWorkingTypeIndex));
                    PersistTradingPreferences();
                }
            }
        }

        public string OrderTimeInForce
        {
            get => _orderTimeInForce;
            set
            {
                if (SetProperty(ref _orderTimeInForce, value))
                {
                    OnPropertyChanged(nameof(OrderTimeInForceIndex));
                    PersistTradingPreferences();
                }
            }
        }

        public bool OrderReduceOnly
        {
            get => _orderReduceOnly;
            set => SetProperty(ref _orderReduceOnly, value);
        }

        public bool IsConditionalOrder => _orderEntryMode == OrderEntryMode.Conditional;

        public bool IsLimitOrderEntry
        {
            get => _orderEntryMode == OrderEntryMode.Limit;
            set
            {
                if (value)
                {
                    SelectedOrderEntryMode = OrderEntryMode.Limit;
                }
            }
        }

        public bool IsMarketOrderEntry
        {
            get => _orderEntryMode == OrderEntryMode.Market;
            set
            {
                if (value)
                {
                    SelectedOrderEntryMode = OrderEntryMode.Market;
                }
            }
        }

        public bool IsConditionalOrderEntry
        {
            get => _orderEntryMode == OrderEntryMode.Conditional;
            set
            {
                if (value)
                {
                    SelectedOrderEntryMode = OrderEntryMode.Conditional;
                }
            }
        }

        public OrderEntryMode SelectedOrderEntryMode
        {
            get => _orderEntryMode;
            set
            {
                if (SetProperty(ref _orderEntryMode, value))
                {
                    OnPropertyChanged(nameof(IsLimitOrder));
                    OnPropertyChanged(nameof(IsMarketOrder));
                    OnPropertyChanged(nameof(IsConditionalOrder));
                    OnPropertyChanged(nameof(IsLimitOrderEntry));
                    OnPropertyChanged(nameof(IsMarketOrderEntry));
                    OnPropertyChanged(nameof(IsConditionalOrderEntry));
                    OnPropertyChanged(nameof(IsConditionalLimit));
                    OnPropertyChanged(nameof(IsConditionalMarket));
                    OnPropertyChanged(nameof(ConditionalExecutionModeIndex));
                    OnPropertyChanged(nameof(StopWorkingTypeIndex));
                    OnPropertyChanged(nameof(OrderTimeInForceIndex));
                    UpdateTotalCost();
                    PersistTradingPreferences();
                }
            }
        }

        public bool IsConditionalLimit
        {
            get => _conditionalUseLimit;
            set
            {
                if (SetProperty(ref _conditionalUseLimit, value))
                {
                    OnPropertyChanged(nameof(IsLimitOrder));
                    OnPropertyChanged(nameof(IsMarketOrder));
                    OnPropertyChanged(nameof(IsConditionalMarket));
                    OnPropertyChanged(nameof(ConditionalExecutionModeIndex));
                    OnPropertyChanged(nameof(OrderTimeInForceIndex));
                    UpdateTotalCost();
                    PersistTradingPreferences();
                }
            }
        }

        public bool IsConditionalMarket
        {
            get => !_conditionalUseLimit;
            set => IsConditionalLimit = !value;
        }

        public int ConditionalExecutionModeIndex
        {
            get => _conditionalUseLimit ? 0 : 1;
            set => IsConditionalLimit = value == 0;
        }

        public int StopWorkingTypeIndex
        {
            get => _stopWorkingType == StopWorkingType.MarkPrice ? 1 : 0;
            set => SelectedStopWorkingType = value == 1 ? StopWorkingType.MarkPrice : StopWorkingType.ContractPrice;
        }

        public int OrderTimeInForceIndex
        {
            get => _orderTimeInForce switch
            {
                "IOC" => 1,
                "FOK" => 2,
                _ => 0,
            };
            set => OrderTimeInForce = value switch
            {
                1 => "IOC",
                2 => "FOK",
                _ => "GTC",
            };
        }

        public string ConditionalOrderInfo =>
            "Условные ордера будут отображаться в списке лимитных или рыночных ордеров, "
            + "когда они достигнут цены активации. Они будут отображаться в списке условных "
            + "ордеров перед их срабатыванием. При срабатывании они будут перемещены в раздел базовых ордеров.";

        public bool IsConditionalInfoOpen
        {
            get => _isConditionalInfoOpen;
            set => SetProperty(ref _isConditionalInfoOpen, value);
        }

        public string PositionsTabHeader => $"ПОЗИЦИИ ({Positions.Count})";
        public string OpenOrdersTabHeader => $"ОТКРЫТЫЕ ОРДЕРА ({OpenOrders.Count})";
        public string BalancesTabHeader => "АКТИВЫ";
        public string TradeHistoryTabHeader => "ИСТОРИЯ СДЕЛОК";

        public ObservableCollection<UserTradeModel> TradeHistory
        {
            get => _tradeHistory;
            set => SetProperty(ref _tradeHistory, value);
        }

        public ObservableCollection<TradeStatsPeriodRow> TradeStatsRows { get; } = [];

        public bool TradeStatsLoading
        {
            get => _tradeStatsLoading;
            private set => SetProperty(ref _tradeStatsLoading, value);
        }

        public string TradeStatsStatusText => TradeStatsLoading
            ? "загрузка…"
            : _tradeStatsFillCount > 0
                ? $"{_tradeStatsFillCount} fills · USDT-M"
                : "нет сделок · USDT-M";

        public IReadOnlyList<ChartIntervalOption> ChartIntervals => ChartIntervalOption.All;

        public int SelectedChartIntervalIndex
        {
            get
            {
                for (var i = 0; i < ChartIntervalOption.All.Count; i++)
                {
                    if (ChartIntervalOption.All[i].ApiInterval == _selectedChartInterval.ApiInterval)
                    {
                        return i;
                    }
                }

                return 0;
            }
            set
            {
                if (value < 0 || value >= ChartIntervalOption.All.Count)
                {
                    return;
                }

                var next = ChartIntervalOption.All[value];
                if (_selectedChartInterval.ApiInterval == next.ApiInterval)
                {
                    return;
                }

                _selectedChartInterval = next;
                OnPropertyChanged(nameof(SelectedChartIntervalIndex));
                OnPropertyChanged(nameof(SelectedChartIntervalLabel));
                PersistChartInterval();
                _ = ChangeChartIntervalAsync();
            }
        }

        private async Task ChangeChartIntervalAsync()
        {
            if (string.IsNullOrEmpty(SelectedSymbol))
            {
                return;
            }

            await ReloadChartAsync(SelectedSymbol);
            await _wsService.UpdateKlineStreamAsync(SelectedSymbol, _selectedChartInterval.StreamSuffix);
        }

        public string SelectedChartIntervalLabel => _selectedChartInterval.Label;

        public bool HideOtherTradeTickers
        {
            get => _hideOtherTradeTickers;
            set
            {
                if (SetProperty(ref _hideOtherTradeTickers, value))
                {
                    ApplyTradeHistoryFilter();
                }
            }
        }

        public int TradeHistoryDays
        {
            get => _tradeHistoryDays;
            set => SetProperty(ref _tradeHistoryDays, value);
        }

        public string TradeHistorySideFilter
        {
            get => _tradeHistorySideFilter;
            set
            {
                if (SetProperty(ref _tradeHistorySideFilter, value))
                {
                    ApplyTradeHistoryFilter();
                }
            }
        }

        public string TradeHistoryPeriodDisplay
        {
            get
            {
                var end = DateTime.Now.Date;
                var start = end.AddDays(-TradeHistoryDays + 1);
                return $"Время сделки {start:yyyy-MM-dd} → {end:yyyy-MM-dd}";
            }
        }

        public string UsdtAvailableDisplay
        {
            get
            {
                var usdt = Balances.FirstOrDefault(b => b.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase));
                return usdt == null ? "—" : usdt.Free.ToString("N2");
            }
        }

        public string UsdtWalletDisplay
        {
            get
            {
                var usdt = Balances.FirstOrDefault(b => b.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase));
                return usdt == null ? "—" : usdt.Total.ToString("N2");
            }
        }

        public double TotalCost
        {
            get => _totalCost;
            set => SetProperty(ref _totalCost, value);
        }

        public bool IsLimitOrder =>
            _orderEntryMode == OrderEntryMode.Limit
            || (_orderEntryMode == OrderEntryMode.Conditional && _conditionalUseLimit);

        public bool IsMarketOrder =>
            _orderEntryMode == OrderEntryMode.Market
            || (_orderEntryMode == OrderEntryMode.Conditional && !_conditionalUseLimit);

        public bool QuantityInUsdt
        {
            get => _quantityInputMode != QuantityInputMode.Contracts;
            set => SetQuantityMode(value ? QuantityInputMode.UsdtOrderSize : QuantityInputMode.Contracts);
        }

        public bool QuantityInContracts
        {
            get => _quantityInputMode == QuantityInputMode.Contracts;
            set { if (value) SetQuantityMode(QuantityInputMode.Contracts); }
        }

        public QuantityInputMode SelectedQuantityMode
        {
            get => _quantityInputMode;
            private set => SetQuantityMode(value);
        }

        public bool IsUnitSelectorOpen
        {
            get => _isUnitSelectorOpen;
            set => SetProperty(ref _isUnitSelectorOpen, value);
        }

        public bool IsContractsMode => _quantityInputMode == QuantityInputMode.Contracts;
        public bool IsUsdtOrderSizeMode => _quantityInputMode == QuantityInputMode.UsdtOrderSize;
        public bool IsUsdtInitialMarginMode => _quantityInputMode == QuantityInputMode.UsdtInitialMargin;
        public bool IsUsdtQuantityMode => _quantityInputMode != QuantityInputMode.Contracts;

        public string SelectedBaseAsset => GetSelectedSymbolInfo()?.BaseAsset ?? "—";
        public string QuantityUnitButtonText => IsContractsMode ? SelectedBaseAsset : "USDT";
        public int ExchangeSymbolLeverage => _exchangeSymbolLeverage;
        public int EffectiveLeverage =>
            RiskManager.CapLeverage(_exchangeSymbolLeverage, AppServices.Settings.MaxLeverage);
        public int CurrentSymbolLeverage => EffectiveLeverage;
        public string LeverageDisplay => $"{EffectiveLeverage}x";
        public string MarginModeDisplay => _symbolMarginMode.ToButtonLabel();

        public string QuantityLabel => "Кол.";

        public string QuantityHint => _quantityInputMode switch
        {
            QuantityInputMode.Contracts => $"Размер ордера в {SelectedBaseAsset}",
            QuantityInputMode.UsdtOrderSize => "Размер ордера в USDT",
            QuantityInputMode.UsdtInitialMargin => $"Начальная маржа USDT · плечо {EffectiveLeverage}x",
            _ => string.Empty,
        };

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
        public ICommand SelectContractsModeCommand { get; }
        public ICommand SelectUsdtOrderSizeCommand { get; }
        public ICommand SelectUsdtInitialMarginCommand { get; }
        public ICommand OpenAdjustLeverageCommand { get; }
        public ICommand OpenAdjustMarginModeCommand { get; }
        public ICommand ClosePositionMarketCommand { get; }
        public ICommand ClosePositionLimitCommand { get; }
        public ICommand CloseAllPositionsCommand { get; }
        public ICommand SearchTradeHistoryCommand { get; }
        public ICommand ResetTradeHistoryFiltersCommand { get; }
        public ICommand SetTradeHistoryDaysCommand { get; }
        public ICommand OpenConditionalInfoLinkCommand { get; }
        public ICommand ToggleConditionalInfoCommand { get; }
        #endregion

        public MainViewModel()
        {
            Action<string> uiLogger = message => AppServices.Log.Info(message);

            _apiService = new BinanceApiService(uiLogger);
            _tradeStatsLoader = new TradeStatsLoader(_apiService, msg => AppServices.Log.Info(msg));
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
            SelectContractsModeCommand = new RelayCommand(_ => SetQuantityMode(QuantityInputMode.Contracts));
            SelectUsdtOrderSizeCommand = new RelayCommand(_ => SetQuantityMode(QuantityInputMode.UsdtOrderSize));
            SelectUsdtInitialMarginCommand = new RelayCommand(_ => SetQuantityMode(QuantityInputMode.UsdtInitialMargin));
            OpenAdjustLeverageCommand = new RelayCommand(() => _ = OpenAdjustLeverageAsync());
            OpenAdjustMarginModeCommand = new RelayCommand(() => _ = OpenAdjustMarginModeAsync());
            ClosePositionMarketCommand = new RelayCommand(async p => await ClosePositionAsync(p, market: true));
            ClosePositionLimitCommand = new RelayCommand(async p => await ClosePositionAsync(p, market: false));
            CloseAllPositionsCommand = new RelayCommand(async () => await CloseAllPositionsAsync(), () => Positions.Count > 0);
            SearchTradeHistoryCommand = new RelayCommand(async () => await RefreshTradeHistoryAsync());
            ResetTradeHistoryFiltersCommand = new RelayCommand(() => ResetTradeHistoryFilters());
            SetTradeHistoryDaysCommand = new RelayCommand(p =>
            {
                if (p is string daysText && int.TryParse(daysText, out var days))
                {
                    TradeHistoryDays = days;
                    OnPropertyChanged(nameof(TradeHistoryPeriodDisplay));
                }
            });
            OpenConditionalInfoLinkCommand = new RelayCommand(_ => OpenConditionalInfoLink());
            ToggleConditionalInfoCommand = new RelayCommand(_ => IsConditionalInfoOpen = !IsConditionalInfoOpen);

            AppServices.Log.Info("Hermes.BinanceDemoFuturesTerminal запущен.");
            InitializeBridge();
            InitializeTradeStatsPlaceholders();
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
            _orderEntryMode = ParseOrderEntryMode(settings);
            _conditionalUseLimit = settings.ConditionalUseLimit;
            _stopWorkingType = ParseStopWorkingType(settings.StopWorkingType);
            _orderTimeInForce = string.IsNullOrWhiteSpace(settings.OrderTimeInForce) ? "GTC" : settings.OrderTimeInForce;
            _orderReduceOnly = settings.OrderReduceOnly;
            _quantityInputMode = ParseQuantityInputMode(settings);
            _selectedChartInterval = ChartIntervalOption.Parse(settings.ChartInterval);
            OnPropertyChanged(nameof(SelectedOrderEntryMode));
            OnPropertyChanged(nameof(IsLimitOrder));
            OnPropertyChanged(nameof(IsMarketOrder));
            OnPropertyChanged(nameof(IsConditionalOrder));
            OnPropertyChanged(nameof(IsLimitOrderEntry));
            OnPropertyChanged(nameof(IsMarketOrderEntry));
            OnPropertyChanged(nameof(IsConditionalOrderEntry));
            OnPropertyChanged(nameof(IsConditionalLimit));
            OnPropertyChanged(nameof(IsConditionalMarket));
            OnPropertyChanged(nameof(SelectedStopWorkingType));
            OnPropertyChanged(nameof(OrderTimeInForce));
            OnPropertyChanged(nameof(OrderReduceOnly));
            NotifyQuantityModeChanged();
            OnPropertyChanged(nameof(SelectedChartIntervalIndex));
            OnPropertyChanged(nameof(SelectedChartIntervalLabel));
            UpdateTotalCost();
        }

        private static OrderEntryMode ParseOrderEntryMode(PlatformSettings settings)
        {
            if (Enum.TryParse<OrderEntryMode>(settings.OrderEntryMode, out var mode))
            {
                return mode;
            }

            return settings.IsLimitOrder ? OrderEntryMode.Limit : OrderEntryMode.Market;
        }

        private static StopWorkingType ParseStopWorkingType(string value) =>
            Enum.TryParse<StopWorkingType>(value, out var mode) ? mode : StopWorkingType.ContractPrice;

        private static QuantityInputMode ParseQuantityInputMode(PlatformSettings settings)
        {
            if (Enum.TryParse<QuantityInputMode>(settings.QuantityInputMode, out var mode))
            {
                return mode;
            }

            return settings.QuantityInUsdt ? QuantityInputMode.UsdtOrderSize : QuantityInputMode.Contracts;
        }

        private void SetQuantityMode(QuantityInputMode mode)
        {
            if (_quantityInputMode == mode)
            {
                IsUnitSelectorOpen = false;
                return;
            }

            _quantityInputMode = mode;
            NotifyQuantityModeChanged();
            IsUnitSelectorOpen = false;
            UpdateTotalCost();
            PersistTradingPreferences();
            ApplyDefaultOrderQuantity();
        }

        private void NotifyQuantityModeChanged()
        {
            OnPropertyChanged(nameof(SelectedQuantityMode));
            OnPropertyChanged(nameof(QuantityInUsdt));
            OnPropertyChanged(nameof(QuantityInContracts));
            OnPropertyChanged(nameof(IsContractsMode));
            OnPropertyChanged(nameof(IsUsdtOrderSizeMode));
            OnPropertyChanged(nameof(IsUsdtInitialMarginMode));
            OnPropertyChanged(nameof(IsUsdtQuantityMode));
            OnPropertyChanged(nameof(QuantityUnitButtonText));
            OnPropertyChanged(nameof(QuantityLabel));
            OnPropertyChanged(nameof(QuantityHint));
        }

        private void PersistTradingPreferences()
        {
            var settings = AppServices.Settings;
            var modeText = _orderEntryMode.ToString();
            if (settings.OrderEntryMode == modeText
                && settings.IsLimitOrder == (_orderEntryMode == OrderEntryMode.Limit)
                && settings.ConditionalUseLimit == _conditionalUseLimit
                && settings.StopWorkingType == _stopWorkingType.ToString()
                && settings.OrderTimeInForce == _orderTimeInForce
                && settings.QuantityInputMode == _quantityInputMode.ToString())
            {
                return;
            }

            settings.OrderEntryMode = modeText;
            settings.IsLimitOrder = _orderEntryMode == OrderEntryMode.Limit;
            settings.ConditionalUseLimit = _conditionalUseLimit;
            settings.StopWorkingType = _stopWorkingType.ToString();
            settings.OrderTimeInForce = _orderTimeInForce;
            settings.QuantityInputMode = _quantityInputMode.ToString();
            settings.QuantityInUsdt = _quantityInputMode != QuantityInputMode.Contracts;
            AppServices.SaveSettings(settings);
            AppServices.Log.Info(
                $"Торговые настройки сохранены: {modeText}, {_quantityInputMode}.");
        }

        private void PersistChartInterval()
        {
            var settings = AppServices.Settings;
            var interval = _selectedChartInterval.ApiInterval;
            if (settings.ChartInterval == interval)
            {
                return;
            }

            settings.ChartInterval = interval;
            AppServices.SaveSettings(settings);
            AppServices.Log.Info($"Таймфрейм графика сохранён: {interval}.");
        }

        private void OpenSettings()
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(ApplyCredentials) { Owner = Application.Current.MainWindow };
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                NotifyLeverageChanged();
                ApplyDefaultOrderQuantity();
            };
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

        private async Task ReloadChartAsync(string? symbol = null)
        {
            symbol ??= SelectedSymbol;
            if (string.IsNullOrEmpty(symbol))
            {
                return;
            }

            var historicalCandles = await _apiService.GetKlinesAsync(
                symbol,
                _selectedChartInterval.ApiInterval,
                _selectedChartInterval.CandleLimit);

            RunOnUIThread(() =>
            {
                Candles.Clear();
                foreach (var candle in historicalCandles)
                {
                    Candles.Add(candle);
                }
            });
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

            // 1. Загрузка исторических свечей через REST
            await ReloadChartAsync(symbol);
            var depthSnapshot = await _apiService.GetDepthAsync(symbol, 20);
            var leverage = 20;
            var marginMode = FuturesMarginType.Cross;
            if (!string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_secretKey))
            {
                leverage = await LoadSymbolLeverageAsync(symbol);
                marginMode = await _apiService.GetSymbolMarginTypeAsync(symbol);
                await ApplySymbolMarginDefaultAsync(symbol);
                marginMode = await _apiService.GetSymbolMarginTypeAsync(symbol);
            }

            RunOnUIThread(() =>
            {
                if (depthSnapshot != null)
                {
                    ApplyDepth(depthSnapshot);
                }

                _exchangeSymbolLeverage = leverage;
                _symbolMarginMode = marginMode;
                NotifyLeverageChanged();
                NotifyMarginModeChanged();
                OnPropertyChanged(nameof(SelectedBaseAsset));
                OnPropertyChanged(nameof(QuantityUnitButtonText));
                ApplyDefaultOrderQuantity();
            });

            // 2. Подписка на стримы в WebSockets
            await _wsService.SubscribeSymbolAsync(symbol, _selectedChartInterval.StreamSuffix);

            // 3. Обновление истории ордеров по этой паре
            if (!string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_secretKey))
            {
                await RefreshOrdersHistoryAsync(symbol);
                await RefreshTradeHistoryAsync();
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
                if (string.IsNullOrEmpty(OrderPrice) && (_orderEntryMode == OrderEntryMode.Limit || IsConditionalLimit))
                {
                    OrderPrice = payload.LastPrice;
                }

                if (string.IsNullOrEmpty(OrderStopPrice) && IsConditionalOrder)
                {
                    OrderStopPrice = payload.LastPrice;
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
                foreach (var p in positions)
                {
                    var symbolInfo = _allSymbols.FirstOrDefault(s =>
                        s.Symbol.Equals(p.Symbol, StringComparison.OrdinalIgnoreCase));
                    p.ContractBadge = GetContractBadge(symbolInfo);
                    p.InitializeCloseFields(
                        symbolInfo?.FormatQuantity(Math.Abs(p.Size)),
                        symbolInfo?.FormatPrice(p.MarkPrice));
                    Positions.Add(p);
                }

                OpenOrders.Clear();
                foreach (var o in openOrders)
                {
                    OpenOrders.Add(MapBinanceOrderToModel(o));
                }

                OnPropertyChanged(nameof(PositionsTabHeader));
                OnPropertyChanged(nameof(OpenOrdersTabHeader));
                CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(UsdtAvailableDisplay));
                OnPropertyChanged(nameof(UsdtWalletDisplay));
            });

            // История ордеров для текущей выбранной пары
            if (!string.IsNullOrEmpty(SelectedSymbol))
            {
                await RefreshOrdersHistoryAsync(SelectedSymbol);
            }

            await RefreshTradeHistoryAsync();
            await RefreshTradeStatsAsync();
            NotifyBridgePublish();
        }

        private async Task RefreshTradeHistoryAsync()
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                return;
            }

            AddLog("Загрузка истории сделок...");
            var startTime = DateTimeOffset.UtcNow.AddDays(-TradeHistoryDays).ToUnixTimeMilliseconds();
            var symbols = GetTradeHistorySymbols();
            _tradeHistoryCache.Clear();

            foreach (var symbol in symbols)
            {
                _knownTradedSymbols.Add(symbol);
                var rows = await _apiService.GetUserTradesAsync(symbol, startTime);
                foreach (var row in rows)
                {
                    _knownTradedSymbols.Add(row.Symbol);
                    _tradeHistoryCache.Add(MapUserTrade(row));
                }
            }

            RunOnUIThread(ApplyTradeHistoryFilter);
        }

        private async Task RefreshTradeStatsAsync()
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                AppServices.Log.Warn("[trade-stats] skip: API keys not configured");
                return;
            }

            TradeStatsLoading = true;
            AppServices.Log.Info("[trade-stats] refresh started");
            try
            {
                var seedSymbols = GetTradeStatsSeedSymbols();
                AppServices.Log.Info(
                    $"[trade-stats] seed symbols: [{string.Join(", ", seedSymbols.OrderBy(s => s))}]");

                var result = await _tradeStatsLoader.LoadAsync(seedSymbols).ConfigureAwait(false);

                foreach (var trade in result.Trades)
                {
                    _knownTradedSymbols.Add(trade.Symbol);
                }

                _tradeStatsSource.Clear();
                _tradeStatsSource.AddRange(result.Trades);
                _tradeStatsFillCount = result.Trades.Count;

                var rows = TradeStatsCalculator.Build(
                    result.Trades,
                    result.IncomeRecords,
                    msg => AppServices.Log.Info(msg));

                RunOnUIThread(() => ApplyTradeStatsRows(rows));
                AppServices.Log.Info(
                    $"[trade-stats] refresh done fills={result.Trades.Count} symbols={result.SymbolCount} "
                    + $"incomeRows={result.IncomeRowCount}");
            }
            catch (Exception ex)
            {
                AppServices.Log.Warn($"[trade-stats] refresh failed: {ex.Message}");
                AppServices.Log.Warn($"[trade-stats] stack: {ex.StackTrace}");
            }
            finally
            {
                RunOnUIThread(() =>
                {
                    TradeStatsLoading = false;
                    OnPropertyChanged(nameof(TradeStatsStatusText));
                });
            }
        }

        private void ApplyTradeStatsRows(IReadOnlyList<TradeStatsPeriodRow> rows)
        {
            TradeStatsRows.Clear();
            foreach (var row in rows)
            {
                TradeStatsRows.Add(row);
                AppServices.Log.Info(
                    $"[trade-stats] UI «{row.PeriodLabel}»: pnl={row.RealizedPnl:F6} comm={row.Commission:F6} "
                    + $"(display {row.PnlDisplay} / {row.CommissionDisplay})");
            }

            OnPropertyChanged(nameof(TradeStatsStatusText));
        }

        private HashSet<string> GetTradeStatsSeedSymbols()
        {
            var symbols = new HashSet<string>(_knownTradedSymbols, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(SelectedSymbol))
            {
                symbols.Add(SelectedSymbol);
            }

            foreach (var position in Positions)
            {
                symbols.Add(position.Symbol);
            }

            foreach (var order in OrderHistory)
            {
                symbols.Add(order.Symbol);
            }

            foreach (var order in OpenOrders)
            {
                symbols.Add(order.Symbol);
            }

            foreach (var trade in _tradeHistoryCache)
            {
                symbols.Add(trade.Symbol);
            }

            return symbols;
        }

        private void InitializeTradeStatsPlaceholders()
        {
            TradeStatsRows.Clear();
            foreach (var label in new[] { "День", "Неделя", "Месяц", "Все время" })
            {
                TradeStatsRows.Add(new TradeStatsPeriodRow
                {
                    PeriodLabel = label,
                    RealizedPnl = 0,
                    Commission = 0,
                });
            }
        }

        private HashSet<string> GetTradeHistorySymbols()
        {
            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(SelectedSymbol))
            {
                symbols.Add(SelectedSymbol);
            }

            if (!HideOtherTradeTickers)
            {
                foreach (var position in Positions)
                {
                    symbols.Add(position.Symbol);
                }

                foreach (var order in OrderHistory)
                {
                    symbols.Add(order.Symbol);
                }

                foreach (var order in OpenOrders)
                {
                    symbols.Add(order.Symbol);
                }
            }

            return symbols;
        }

        private UserTradeModel MapUserTrade(UserTradeResponse raw)
        {
            var symbolInfo = _allSymbols.FirstOrDefault(s =>
                s.Symbol.Equals(raw.Symbol, StringComparison.OrdinalIgnoreCase));
            return new UserTradeModel
            {
                TradeId = raw.Id,
                OrderId = raw.OrderId,
                Time = DateTimeOffset.FromUnixTimeMilliseconds(raw.Time).DateTime.ToLocalTime(),
                Symbol = raw.Symbol,
                ContractBadge = GetContractBadge(symbolInfo),
                IsBuy = raw.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase),
                Price = double.Parse(raw.Price, CultureInfo.InvariantCulture),
                QuoteQty = double.Parse(raw.QuoteQty, CultureInfo.InvariantCulture),
                Commission = double.Parse(raw.Commission, CultureInfo.InvariantCulture),
                CommissionAsset = raw.CommissionAsset,
                IsMaker = raw.Maker,
                RealizedPnl = double.Parse(raw.RealizedPnl, CultureInfo.InvariantCulture),
            };
        }

        private void ApplyTradeHistoryFilter()
        {
            TradeHistory.Clear();
            IEnumerable<UserTradeModel> query = _tradeHistoryCache;
            if (HideOtherTradeTickers && !string.IsNullOrEmpty(SelectedSymbol))
            {
                query = query.Where(t => t.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase));
            }

            if (TradeHistorySideFilter.Equals("Buy", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.IsBuy);
            }
            else if (TradeHistorySideFilter.Equals("Sell", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => !t.IsBuy);
            }

            foreach (var trade in query.OrderByDescending(t => t.Time))
            {
                TradeHistory.Add(trade);
            }
        }

        private void ResetTradeHistoryFilters()
        {
            HideOtherTradeTickers = true;
            TradeHistoryDays = 7;
            TradeHistorySideFilter = "All";
            OnPropertyChanged(nameof(TradeHistoryPeriodDisplay));
            _ = RefreshTradeHistoryAsync();
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

            if (IsConditionalOrder)
            {
                await ExecuteConditionalOrderAsync(side);
                return;
            }

            var leverageError = await ValidateLeverageBeforeOrderAsync(SelectedSymbol);
            if (leverageError != null)
            {
                AppServices.Log.Warn(leverageError);
                MessageBox.Show(leverageError, "Риск-менеджер", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                AppServices.Settings,
                notional,
                RiskManager.GetWalletBalanceUsdt(Balances),
                EffectiveLeverage,
                positionsList,
                SelectedSymbol,
                isNew);
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
            var protectionNote = BuildProtectionLogNote(stopLossPrice, takeProfitPrice);
            AddLog(
                OrderVolumeUsdtHelper.FormatOrderLog(side, orderType, SelectedSymbol, notional, priceText ?? "MARKET")
                + protectionNote);

            try
            {
                var response = await _apiService.PlaceOrderAsync(
                    SelectedSymbol, side, orderType, qtyText, priceText, timeInForce: _orderTimeInForce);
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

        private async Task ExecuteConditionalOrderAsync(string side)
        {
            var leverageError = await ValidateLeverageBeforeOrderAsync(SelectedSymbol);
            if (leverageError != null)
            {
                AppServices.Log.Warn(leverageError);
                MessageBox.Show(leverageError, "Риск-менеджер", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var symbolInfo = GetSelectedSymbolInfo();
            if (symbolInfo == null)
            {
                return;
            }

            if (!double.TryParse(OrderStopPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var stopRaw)
                || stopRaw <= 0)
            {
                MessageBox.Show("Введите корректную стоп-цену.", "Неверный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var stopText = symbolInfo.FormatPrice(stopRaw);
            var stopPrice = double.Parse(stopText, CultureInfo.InvariantCulture);
            var currentPrice = GetMarketReferencePrice();
            if (currentPrice <= 0)
            {
                MessageBox.Show("Нет текущей цены для проверки стоп-цены.", "Неверный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ValidateStopTriggerPrice(side, stopPrice, currentPrice, out var stopError))
            {
                MessageBox.Show(stopError, "Стоп-цена", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? priceText = null;
            if (IsConditionalLimit)
            {
                if (!double.TryParse(OrderPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var limitRaw)
                    || limitRaw <= 0)
                {
                    MessageBox.Show("Введите корректную цену лимитного ордера.", "Неверный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                priceText = symbolInfo.FormatPrice(limitRaw);
            }

            if (!TryResolveContractQuantity(out var qty, out var qtyText, out var error))
            {
                MessageBox.Show(error, "Неверный ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var priceForRisk = priceText != null
                ? double.Parse(priceText, CultureInfo.InvariantCulture)
                : stopPrice;
            var notional = RiskManager.EstimateNotionalUsdt(qty, priceForRisk);
            var positionsList = Positions.ToList();
            var isNew = !OrderReduceOnly
                && !positionsList.Any(p => p.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase));
            if (!OrderReduceOnly)
            {
                var riskError = RiskManager.ValidateOrder(
                    AppServices.Settings,
                    notional,
                    RiskManager.GetWalletBalanceUsdt(Balances),
                    EffectiveLeverage,
                    positionsList,
                    SelectedSymbol,
                    isNew);
                if (riskError != null)
                {
                    AppServices.Log.Warn(riskError);
                    MessageBox.Show(riskError, "Риск-менеджер", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var isLong = side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
            if (UseOrderSlTp
                && !TryBuildProtectionPlan(isLong, priceForRisk, out _, out _, out error))
            {
                MessageBox.Show(error, "SL / TP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var orderType = IsConditionalLimit ? "STOP" : "STOP_MARKET";
            var workingType = _stopWorkingType == StopWorkingType.MarkPrice ? "MARK_PRICE" : "CONTRACT_PRICE";
            AddLog(
                OrderVolumeUsdtHelper.FormatOrderLog(side, orderType, SelectedSymbol, notional, priceText ?? stopText)
                + $" стоп={stopText} ({workingType})");

            try
            {
                var response = await _apiService.PlaceStopOrderAsync(
                    SelectedSymbol,
                    side,
                    orderType,
                    qtyText,
                    stopText,
                    priceText,
                    workingType,
                    _orderTimeInForce,
                    OrderReduceOnly);

                if (response != null)
                {
                    MessageBox.Show(
                        $"Условный ордер размещён!\nID: {response.OrderId}\nСтатус: {response.Status}",
                        "Успех",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    ApplyDefaultOrderQuantity();
                    await RefreshAccountDataAsync();
                }
            }
            catch (Exception ex)
            {
                AppServices.Log.Error($"PlaceConditionalOrder: {ex.Message}");
                MessageBox.Show($"Ошибка размещения условного ордера:\n{ex.Message}", "Ошибка API", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool ValidateStopTriggerPrice(string side, double stopPrice, double currentPrice, out string error)
        {
            var isBuy = side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
            if (isBuy && stopPrice <= currentPrice)
            {
                error = "Для BUY стоп-цена должна быть выше текущей цены.";
                return false;
            }

            if (!isBuy && stopPrice >= currentPrice)
            {
                error = "Для SELL стоп-цена должна быть ниже текущей цены.";
                return false;
            }

            error = string.Empty;
            return true;
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
            double price = GetOrderPriceEstimate();

            switch (_quantityInputMode)
            {
                case QuantityInputMode.UsdtOrderSize:
                    OrderQuantity = (availableQuote * percent).ToString("F2", CultureInfo.InvariantCulture);
                    return;
                case QuantityInputMode.UsdtInitialMargin:
                    OrderQuantity = (availableQuote * percent).ToString("F2", CultureInfo.InvariantCulture);
                    return;
                default:
                    if (price <= 0)
                    {
                        return;
                    }

                    var maxContracts = (availableQuote * EffectiveLeverage) / price;
                    OrderQuantity = currentSymbolInfo.FormatQuantity(maxContracts * percent);
                    return;
            }
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
                StopPrice = double.TryParse(raw.StopPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var sp) ? sp : 0,
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

            if (price <= 0 || symbolInfo == null)
            {
                TotalCost = 0.0;
                ContractQtyPreview = string.Empty;
                return;
            }

            var notional = ResolveNotionalUsdt(input, price);
            TotalCost = notional;
            var contracts = symbolInfo.EnsureMinNotionalQuantity(notional / price, price);

            ContractQtyPreview = _quantityInputMode switch
            {
                QuantityInputMode.Contracts => contracts > 0
                    ? $"≈ {notional.ToString("F2", CultureInfo.InvariantCulture)} USDT"
                    : string.Empty,
                QuantityInputMode.UsdtOrderSize => contracts > 0
                    ? $"≈ {symbolInfo.FormatQuantity(contracts)} {symbolInfo.BaseAsset}"
                    : string.Empty,
                QuantityInputMode.UsdtInitialMargin => contracts > 0
                    ? $"≈ {symbolInfo.FormatQuantity(contracts)} {symbolInfo.BaseAsset} · номинал {notional.ToString("F2", CultureInfo.InvariantCulture)} USDT"
                    : string.Empty,
                _ => string.Empty,
            };
        }

        private double ResolveNotionalUsdt(double input, double price) =>
            _quantityInputMode switch
            {
                QuantityInputMode.Contracts => input * price,
                QuantityInputMode.UsdtOrderSize => input,
                QuantityInputMode.UsdtInitialMargin => input * EffectiveLeverage,
                _ => input,
            };

        private double GetMarketReferencePrice()
        {
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

        private double GetOrderPriceEstimate()
        {
            if (IsConditionalOrder
                && double.TryParse(OrderStopPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var stopPrice)
                && stopPrice > 0)
            {
                if (IsConditionalLimit
                    && double.TryParse(OrderPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var limitPrice)
                    && limitPrice > 0)
                {
                    return limitPrice;
                }

                return stopPrice;
            }

            if (IsLimitOrder
                && double.TryParse(OrderPrice.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double orderLimitPrice)
                && orderLimitPrice > 0)
            {
                return orderLimitPrice;
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
                error = _quantityInputMode switch
                {
                    QuantityInputMode.UsdtInitialMargin => "Введите корректную начальную маржу в USDT.",
                    QuantityInputMode.UsdtOrderSize => "Введите корректную сумму в USDT.",
                    _ => $"Введите корректное количество в {SelectedBaseAsset}.",
                };
                return false;
            }

            var symbolInfo = GetSelectedSymbolInfo();
            if (symbolInfo == null)
            {
                error = "Символ не выбран.";
                return false;
            }

            var price = GetOrderPriceEstimate();
            if (price <= 0)
            {
                error = "Нет цены для расчёта количества. Дождитесь тикера или укажите цену.";
                return false;
            }

            var notional = ResolveNotionalUsdt(input, price);
            qty = symbolInfo.EnsureMinNotionalQuantity(notional / price, price);

            if (qty <= 0)
            {
                error = _quantityInputMode == QuantityInputMode.Contracts
                    ? "Количество меньше минимального шага лота."
                    : "Сумма слишком мала для минимального лота.";
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

        private static void OpenConditionalInfoLink()
        {
            Process.Start(new ProcessStartInfo(
                "https://www.binance.com/ru/support/faq/detail/998e63d2587d4e2fb894b1615de3b288")
            {
                UseShellExecute = true,
            });
        }

        private void NotifyLeverageChanged()
        {
            OnPropertyChanged(nameof(ExchangeSymbolLeverage));
            OnPropertyChanged(nameof(EffectiveLeverage));
            OnPropertyChanged(nameof(CurrentSymbolLeverage));
            OnPropertyChanged(nameof(LeverageDisplay));
            OnPropertyChanged(nameof(QuantityHint));
            UpdateTotalCost();
        }

        private void NotifyMarginModeChanged() =>
            OnPropertyChanged(nameof(MarginModeDisplay));

        private async Task<string?> ValidateLeverageBeforeOrderAsync(string symbol)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                return null;
            }

            var exchangeLev = await _apiService.GetSymbolLeverageAsync(symbol);
            RunOnUIThread(() =>
            {
                _exchangeSymbolLeverage = exchangeLev;
                NotifyLeverageChanged();
            });

            if (!AppServices.Settings.RiskManagementEnabled || AppServices.Settings.MaxLeverage <= 0)
            {
                return null;
            }

            if (exchangeLev > AppServices.Settings.MaxLeverage)
            {
                return
                    $"Плечо {exchangeLev}x превышает лимит риск-менеджера {AppServices.Settings.MaxLeverage}x. Нажмите «{LeverageDisplay}» над формой ордера и снизьте плечо.";
            }

            return null;
        }

        private async Task OpenAdjustLeverageAsync()
        {
            if (string.IsNullOrEmpty(SelectedSymbol))
            {
                return;
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                MessageBox.Show(
                    "Для изменения плеча сохраните API-ключи Demo в «Настройках».",
                    "Кредитное плечо",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_adjustLeverageWindow is { IsVisible: true })
            {
                _adjustLeverageWindow.Activate();
                return;
            }

            var exchangeLev = await _apiService.GetSymbolLeverageAsync(SelectedSymbol);
            var brackets = await _apiService.GetLeverageBracketsAsync(SelectedSymbol);
            var symbolMax = LeverageBracketHelper.GetSymbolMaxLeverage(brackets);
            var riskMax = AppServices.Settings.RiskManagementEnabled && AppServices.Settings.MaxLeverage > 0
                ? AppServices.Settings.MaxLeverage
                : 0;
            var maxSelectable = riskMax > 0 ? Math.Min(symbolMax, riskMax) : symbolMax;
            var displayLeverage = RiskManager.CapLeverage(exchangeLev, riskMax > 0 ? riskMax : exchangeLev);

            RunOnUIThread(() =>
            {
                _exchangeSymbolLeverage = exchangeLev;
                NotifyLeverageChanged();

                var vm = new AdjustLeverageViewModel(
                    SelectedSymbol,
                    displayLeverage,
                    maxSelectable,
                    symbolMax,
                    brackets,
                    AppServices.Settings);

                _adjustLeverageWindow = new AdjustLeverageWindow(
                    vm,
                    (lev, applyToAll) => ApplyLeverageAsync(SelectedSymbol, lev, applyToAll))
                {
                    Owner = Application.Current.MainWindow,
                };
                _adjustLeverageWindow.Closed += (_, _) => _adjustLeverageWindow = null;
                _adjustLeverageWindow.ShowDialog();
            });
        }

        private async Task<bool> ApplyLeverageAsync(string symbol, int leverage, bool applyToAllSymbols)
        {
            if (AppServices.Settings.RiskManagementEnabled && AppServices.Settings.MaxLeverage > 0)
            {
                leverage = RiskManager.CapLeverage(leverage, AppServices.Settings.MaxLeverage);
            }

            var ok = await _apiService.SetLeverageAsync(symbol, leverage);
            if (!ok)
            {
                return false;
            }

            var settings = AppServices.Settings;
            settings.ApplyDefaultLeverageToAllSymbols = applyToAllSymbols;
            if (applyToAllSymbols)
            {
                settings.DefaultLeverage = leverage;
            }

            AppServices.SaveSettings(settings);

            if (applyToAllSymbols)
            {
                await ApplyLeverageToAllSymbolsAsync(leverage);
            }

            RunOnUIThread(() =>
            {
                _exchangeSymbolLeverage = leverage;
                NotifyLeverageChanged();
                ApplyDefaultOrderQuantity();
            });
            AddLog(applyToAllSymbols
                ? $"Плечо {leverage}x установлено для всех контрактов (по умолчанию)."
                : $"Плечо {symbol} изменено на {leverage}x.");
            await RefreshAccountDataAsync();
            return true;
        }

        private async Task ApplyLeverageToAllSymbolsAsync(int leverage)
        {
            foreach (var symbol in _allSymbols.Select(s => s.Symbol).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await _apiService.SetLeverageAsync(symbol, leverage);
                }
                catch (Exception ex)
                {
                    AppServices.Log.Warn($"Плечо {symbol}: {ex.Message}");
                }
            }
        }

        private async Task<int> LoadSymbolLeverageAsync(string symbol)
        {
            var leverage = await _apiService.GetSymbolLeverageAsync(symbol);
            var settings = AppServices.Settings;
            var riskMax = settings.RiskManagementEnabled && settings.MaxLeverage > 0
                ? settings.MaxLeverage
                : 0;

            if (settings.ApplyDefaultLeverageToAllSymbols && settings.DefaultLeverage > 0)
            {
                var target = riskMax > 0
                    ? RiskManager.CapLeverage(settings.DefaultLeverage, riskMax)
                    : settings.DefaultLeverage;
                if (leverage != target)
                {
                    await _apiService.SetLeverageAsync(symbol, target);
                    leverage = target;
                }

                return leverage;
            }

            if (riskMax > 0 && leverage > riskMax)
            {
                leverage = riskMax;
            }

            return leverage;
        }

        private async Task ApplySymbolMarginDefaultAsync(string symbol)
        {
            var settings = AppServices.Settings;
            if (!settings.ApplyDefaultMarginTypeToAllSymbols)
            {
                return;
            }

            if (!Enum.TryParse<FuturesMarginType>(settings.DefaultMarginType, out var target))
            {
                return;
            }

            var openOrders = await _apiService.GetOpenOrdersAsync(symbol);
            var hasPosition = Positions.Any(p =>
                p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            if (hasPosition || openOrders.Count > 0)
            {
                return;
            }

            var current = await _apiService.GetSymbolMarginTypeAsync(symbol);
            if (current == target)
            {
                return;
            }

            var (success, _) = await _apiService.SetMarginTypeAsync(symbol, target);
            if (success)
            {
                RunOnUIThread(() =>
                {
                    if (symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase))
                    {
                        _symbolMarginMode = target;
                        NotifyMarginModeChanged();
                    }
                });
            }
        }

        private async Task OpenAdjustMarginModeAsync()
        {
            if (string.IsNullOrEmpty(SelectedSymbol))
            {
                return;
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                MessageBox.Show(
                    "Для изменения режима маржи сохраните API-ключи Demo в «Настройках».",
                    "Режим маржи",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_adjustMarginModeWindow is { IsVisible: true })
            {
                _adjustMarginModeWindow.Activate();
                return;
            }

            var currentMode = await _apiService.GetSymbolMarginTypeAsync(SelectedSymbol);
            var openOrders = await _apiService.GetOpenOrdersAsync(SelectedSymbol);
            var hasPosition = Positions.Any(p =>
                p.Symbol.Equals(SelectedSymbol, StringComparison.OrdinalIgnoreCase));
            var hasOpenPositionOrOrder = hasPosition || openOrders.Count > 0;
            var contractBadge = GetContractBadge(GetSelectedSymbolInfo());

            RunOnUIThread(() =>
            {
                _symbolMarginMode = currentMode;
                NotifyMarginModeChanged();

                var vm = new AdjustMarginModeViewModel(
                    SelectedSymbol,
                    contractBadge,
                    currentMode,
                    hasOpenPositionOrOrder,
                    AppServices.Settings.ApplyDefaultMarginTypeToAllSymbols);

                _adjustMarginModeWindow = new AdjustMarginModeWindow(
                    vm,
                    (mode, applyToAll) => ApplyMarginTypeAsync(SelectedSymbol, mode, applyToAll))
                {
                    Owner = Application.Current.MainWindow,
                };
                _adjustMarginModeWindow.Closed += (_, _) => _adjustMarginModeWindow = null;
                _adjustMarginModeWindow.ShowDialog();
            });
        }

        private async Task<(bool Success, string? Error, bool MarginChanged)> ApplyMarginTypeAsync(
            string symbol,
            FuturesMarginType marginType,
            bool applyToAllSymbols)
        {
            var currentMode = await _apiService.GetSymbolMarginTypeAsync(symbol);
            var marginChanged = currentMode != marginType;

            if (marginChanged)
            {
                var (success, error) = await _apiService.SetMarginTypeAsync(symbol, marginType);
                if (!success)
                {
                    if (BinanceApiService.IsMarginTypeUnchangedError(error))
                    {
                        marginChanged = false;
                    }
                    else
                    {
                        return (false, error, false);
                    }
                }
            }

            var settings = AppServices.Settings;
            settings.ApplyDefaultMarginTypeToAllSymbols = applyToAllSymbols;
            if (applyToAllSymbols)
            {
                settings.DefaultMarginType = marginType.ToString();
            }

            AppServices.SaveSettings(settings);

            if (applyToAllSymbols)
            {
                await ApplyMarginTypeToAllSymbolsAsync(marginType);
            }

            RunOnUIThread(() =>
            {
                _symbolMarginMode = marginType;
                NotifyMarginModeChanged();
            });

            if (marginChanged)
            {
                AddLog(applyToAllSymbols
                    ? $"Режим маржи {marginType.ToMarginLabel()} установлен для всех контрактов (по умолчанию)."
                    : $"Режим маржи {symbol} изменён на {marginType.ToButtonLabel()}.");
            }
            else if (applyToAllSymbols)
            {
                AddLog($"Режим маржи {marginType.ToMarginLabel()} сохранён по умолчанию для всех контрактов.");
            }

            await RefreshAccountDataAsync();
            return (true, null, marginChanged);
        }

        private async Task ApplyMarginTypeToAllSymbolsAsync(FuturesMarginType marginType)
        {
            var openOrders = await _apiService.GetOpenOrdersAsync();
            var blockedSymbols = Positions
                .Select(p => p.Symbol)
                .Concat(openOrders.Select(o => o.Symbol))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var failures = new List<string>();
            foreach (var symbolInfo in _allSymbols
                         .Where(s => s.Status.Equals("TRADING", StringComparison.OrdinalIgnoreCase))
                         .Where(s => s.QuoteAsset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
                         .DistinctBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                var sym = symbolInfo.Symbol;
                if (blockedSymbols.Contains(sym))
                {
                    continue;
                }

                var (ok, err) = await _apiService.SetMarginTypeAsync(sym, marginType);
                if (!ok && !BinanceApiService.IsMarginTypeUnchangedError(err))
                {
                    failures.Add($"{sym}: {err ?? "ошибка API"}");
                    AppServices.Log.Warn($"Режим маржи {sym}: {err ?? "ошибка API"}");
                }
            }

            if (failures.Count > 0)
            {
                AddLog($"Режим маржи: не удалось для {failures.Count} контрактов (см. журнал).");
            }
        }

        private static string GetContractBadge(SymbolInfo? symbolInfo)
        {
            if (symbolInfo == null || string.IsNullOrWhiteSpace(symbolInfo.ContractType))
            {
                return "Бесср";
            }

            return symbolInfo.ContractType.Equals("PERPETUAL", StringComparison.OrdinalIgnoreCase)
                ? "Бесср"
                : symbolInfo.ContractType;
        }

        private async Task ClosePositionAsync(object? param, bool market)
        {
            if (param is not PositionModel position)
            {
                return;
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                MessageBox.Show(
                    "Для закрытия позиций сохраните API-ключи Demo в «Настройках».",
                    "Закрытие позиции",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var symbolInfo = _allSymbols.FirstOrDefault(s =>
                s.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase));
            if (!TryResolveCloseQuantity(position, symbolInfo, out var qty, out var qtyText, out var error))
            {
                MessageBox.Show(error, "Закрытие позиции", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? priceText = null;
            if (!market)
            {
                if (!double.TryParse(position.CloseLimitPriceText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var price)
                    || price <= 0)
                {
                    MessageBox.Show("Введите корректную цену для лимитного закрытия.", "Закрытие позиции", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                priceText = symbolInfo?.FormatPrice(price) ?? price.ToString(CultureInfo.InvariantCulture);
            }

            var side = position.IsLong ? "SELL" : "BUY";
            var orderType = market ? "MARKET" : "LIMIT";
            var closeNotional = qty * (position.MarkPrice > 0 ? position.MarkPrice : GetOrderPriceEstimate());
            AddLog(OrderVolumeUsdtHelper.FormatOrderLog(side, orderType, position.Symbol, closeNotional, priceText) + " (reduceOnly)");

            try
            {
                var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 2000;
                var response = await _apiService.PlaceOrderAsync(position.Symbol, side, orderType, qtyText, priceText, reduceOnly: true);
                var pnl = response is not null
                    ? await CloseRealizedPnlPoller.PollOrderPnlAsync(_apiService, position.Symbol, response.OrderId, startMs)
                    : null;
                var pnlNote = pnl.HasValue
                    ? $" · PnL: {OrderVolumeUsdtHelper.FormatSignedPnl(pnl.Value)}"
                    : string.Empty;
                AddLog($"Позиция {position.Symbol} закрыта ({orderType}){pnlNote}.");
                await RefreshAccountDataAsync();
            }
            catch (Exception ex)
            {
                AppServices.Log.Warn(ex.Message);
                MessageBox.Show(ex.Message, "Закрытие позиции", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task CloseAllPositionsAsync()
        {
            if (Positions.Count == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
            {
                MessageBox.Show(
                    "Для закрытия позиций сохраните API-ключи Demo в «Настройках».",
                    "Закрыть все позиции",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Закрыть все {Positions.Count} позиц(ии/ий) по рынку?",
                "Закрыть все позиции",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var failures = new List<string>();
            foreach (var position in Positions.ToList())
            {
                var symbolInfo = _allSymbols.FirstOrDefault(s =>
                    s.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase));
                var qtyText = symbolInfo?.FormatQuantity(Math.Abs(position.Size))
                              ?? Math.Abs(position.Size).ToString(CultureInfo.InvariantCulture);
                var side = position.IsLong ? "SELL" : "BUY";

                try
                {
                    AddLog($"Закрытие позиции: {side} MARKET {qtyText} {position.Symbol} (reduceOnly, close all)");
                    await _apiService.PlaceOrderAsync(position.Symbol, side, "MARKET", qtyText, reduceOnly: true);
                }
                catch (Exception ex)
                {
                    failures.Add($"{position.Symbol}: {ex.Message}");
                }
            }

            await RefreshAccountDataAsync();

            if (failures.Count > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, failures),
                    "Закрыть все позиции",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                AddLog("Все позиции закрыты по рынку.");
            }
        }

        private static bool TryResolveCloseQuantity(
            PositionModel position,
            SymbolInfo? symbolInfo,
            out double qty,
            out string qtyText,
            out string error)
        {
            qty = 0;
            qtyText = string.Empty;
            error = string.Empty;

            if (!double.TryParse(position.CloseQuantityText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out qty)
                || qty <= 0)
            {
                error = "Введите корректное количество для закрытия.";
                return false;
            }

            var maxQty = Math.Abs(position.Size);
            if (qty > maxQty + 1e-12)
            {
                error = $"Количество не может превышать размер позиции ({symbolInfo?.FormatQuantity(maxQty) ?? maxQty.ToString(CultureInfo.InvariantCulture)}).";
                return false;
            }

            if (symbolInfo != null)
            {
                qty = symbolInfo.RoundQuantity(qty);
                if (qty <= 0)
                {
                    error = "Количество меньше минимального шага инструмента.";
                    return false;
                }

                qtyText = symbolInfo.FormatQuantity(qty);
            }
            else
            {
                qtyText = qty.ToString(CultureInfo.InvariantCulture);
            }

            return true;
        }

        private void ApplyDefaultOrderQuantity()
        {
            var symbolInfo = GetSelectedSymbolInfo();
            if (symbolInfo == null)
            {
                return;
            }

            if (_quantityInputMode == QuantityInputMode.UsdtOrderSize)
            {
                var defaultUsdt = AppServices.Settings.DefaultAgentOrderUsdt;
                if (defaultUsdt > 0)
                {
                    var wallet = RiskManager.GetWalletBalanceUsdt(Balances);
                    var capped = OrderVolumeUsdtHelper.CapNotionalUsdt(
                        defaultUsdt,
                        AppServices.Settings,
                        wallet,
                        EffectiveLeverage);
                    OrderQuantity = capped.ToString("F2", CultureInfo.InvariantCulture);
                    return;
                }
            }

            var price = GetOrderPriceEstimate();
            var defaultInput = symbolInfo.GetDefaultQuantityInput(_quantityInputMode, price, EffectiveLeverage);
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
                UseOrderSlTp ? OrderStopLoss : string.Empty,
                UseOrderSlTp ? OrderTakeProfit : string.Empty,
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
