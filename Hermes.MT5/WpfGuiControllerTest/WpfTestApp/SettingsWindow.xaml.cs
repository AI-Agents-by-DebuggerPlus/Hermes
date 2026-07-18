using System;
using System.Windows;

namespace WpfTestApp
{
    public partial class SettingsWindow : Window
    {
        private bool _syncing;

        public SettingsWindow()
        {
            InitializeComponent();
            Title = BuildInfo.SettingsWindowTitle;
            txtSettingsHeading.Text = "SETTINGS " + BuildInfo.Version;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Loaded += (_, __) => Title = BuildInfo.SettingsWindowTitle;
            btnClose.Click += (_, __) => Close();
        }

        public bool AutoTrade
        {
            get => chkAutoTrade.IsChecked == true;
            set
            {
                _syncing = true;
                try { chkAutoTrade.IsChecked = value; }
                finally { _syncing = false; }
            }
        }

        public bool RealTrade
        {
            get => chkRealTrade.IsChecked == true;
            set
            {
                _syncing = true;
                try { chkRealTrade.IsChecked = value; }
                finally { _syncing = false; }
            }
        }

        public event Action AutoTradeChanged;
        public event Action RealTradeChanged;

        public void WireEvents()
        {
            chkAutoTrade.Checked += OnAutoTradeChanged;
            chkAutoTrade.Unchecked += OnAutoTradeChanged;
            chkRealTrade.Checked += OnRealTradeChanged;
            chkRealTrade.Unchecked += OnRealTradeChanged;
        }

        private void OnAutoTradeChanged(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            AutoTradeChanged?.Invoke();
        }

        private void OnRealTradeChanged(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            RealTradeChanged?.Invoke();
        }
    }
}
