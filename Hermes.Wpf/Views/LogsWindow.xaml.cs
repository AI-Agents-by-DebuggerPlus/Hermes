using System.Collections.ObjectModel;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class LogsWindow : System.Windows.Window
{
    public LogsWindow(LogService logService)
    {
        InitializeComponent();
        Entries = logService.Entries;
        Header = $"Log file: {logService.CurrentLogFilePath}";
        DataContext = this;

        Entries.CollectionChanged += (_, _) =>
        {
            if (Entries.Count > 0)
            {
                LogsListBox.ScrollIntoView(Entries[^1]);
            }
        };
    }

    public ObservableCollection<string> Entries { get; }

    public string Header { get; }
}
