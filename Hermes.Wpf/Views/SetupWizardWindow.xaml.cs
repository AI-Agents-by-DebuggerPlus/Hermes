using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow(ConnectionService connectionService, SettingsService settingsService, HermesSettings settings)
    {
        InitializeComponent();
        DataContext = new SetupWizardViewModel(connectionService, settingsService, settings);
    }
}
