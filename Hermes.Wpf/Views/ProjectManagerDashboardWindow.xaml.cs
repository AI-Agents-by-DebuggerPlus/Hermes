using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class ProjectManagerDashboardWindow : Window
{
    private readonly PortfolioStoreService _store;

    public ProjectManagerDashboardWindow(PortfolioStoreService store)
    {
        InitializeComponent();
        _store = store;
        PathLabel.Text = _store.StorePath;
        _store.Changed += OnChanged;
        Closed += (_, _) => _store.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.Invoke(Refresh);

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var all = _store.GetAll();
        IdeaList.ItemsSource = all.Where(i => i.Category == PortfolioCategory.Idea).Select(Wrap).ToList();
        DevList.ItemsSource = all.Where(i => i.Category == PortfolioCategory.InDevelopment).Select(Wrap).ToList();
        CurrentList.ItemsSource = all.Where(i => i.Category == PortfolioCategory.Current).Select(Wrap).ToList();
    }

    private static Row Wrap(PortfolioInitiative i) => new(i);

    private sealed class Row
    {
        public Row(PortfolioInitiative item) => Item = item;

        public PortfolioInitiative Item { get; }

        public string Line
        {
            get
            {
                var link = string.IsNullOrWhiteSpace(Item.LinkedWorkspace)
                    ? ""
                    : $" → {Item.LinkedWorkspace}";
                return $"{Item.Title}{link} ({Item.Id})";
            }
        }
    }
}
