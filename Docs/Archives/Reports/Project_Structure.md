# Hermes.Wpf Project Structure (Context)

Ниже структура, важная для понимания контекста подключения и логов.

```text
Hermes.Wpf/
  Models/
    ChatMessage.cs
    HermesProject.cs
    SessionHistory.cs
    HermesSettings.cs
    ConnectionState.cs
    ConnectionStatus.cs

  Services/
    HermesService.cs
    ConnectionService.cs
    SettingsService.cs
    ProjectService.cs
    HistoryService.cs
    LogService.cs

  ViewModels/
    MainViewModel.cs
    SetupWizardViewModel.cs
    SettingsViewModel.cs
    ChatViewModel.cs
    ProjectViewModel.cs
    BaseViewModel.cs

  Views/
    MainWindow.xaml
    StatusIndicator.xaml
    SetupWizardWindow.xaml
    SettingsWindow.xaml
    ChatView.xaml
    ProjectPanel.xaml
    TerminalView.xaml
    LogsWindow.xaml
    HelpWindow.xaml

  Converters/
    ConnectionStateToColorConverter.cs

  Commands/
    RelayCommand.cs
```

Логи приложения:

```text
Docs/Logs/hermes_session_*.log
```
