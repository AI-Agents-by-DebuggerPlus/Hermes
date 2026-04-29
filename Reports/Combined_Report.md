# Combined Reports

Этот файл объединяет все материалы из папки `Reports` в один документ.

---

## Source: `README.md`

# Reports Package

Этот пакет собран для анализа проблем подключения Hermes в WPF-приложении.

## Содержимое

- `Connection_Log_Report_2026-04-29.md`  
  Основной отчет по свежим логам и описанию проблемы.

- `Project_Structure.md`  
  Структура проекта, важная для понимания контекста.

- `Code_Context.md`  
  Ключевые фрагменты кода (`ConnectionService`, `HermesService`, `MainViewModel`, настройки).

## Источники логов

- `Docs/Logs/hermes_session_20260429_134601.log`
- `Docs/Logs/hermes_session_20260429_133540.log`

---

## Source: `Connection_Log_Report_2026-04-29.md`

# Connection Log Report (2026-04-29)

## Проверенные логи

- `Docs/Logs/hermes_session_20260429_134601.log`
- `Docs/Logs/hermes_session_20260429_133540.log`

## Наблюдаемая проблема

В текущих логах повторяется одна и та же ошибка подключения:

- `WSL_E_DISTRO_NOT_FOUND`
- `There is no distribution with the supplied name.`

Типичные строки:

```text
2026-04-29 13:46:02.392 [ERROR] [connection] Venv failed: ... WSL_E_DISTRO_NOT_FOUND
2026-04-29 13:46:20.477 [ERROR] [connection] Venv failed: ... WSL_E_DISTRO_NOT_FOUND
```

## Что это означает

Ошибка возникает при вызове `wsl.exe -d "<WslDistro>" ...`, когда WSL не находит переданное имя дистрибутива.

Важный момент: сообщение приходит на шаге `Venv failed`, но первопричина именно в `distro not found`, а не в venv.

## Вероятная причина

- В активной сессии приложения используется некорректное/устаревшее значение `WslDistro` (или продолжает работать старый экземпляр с прежними настройками).
- Из-за `AutoReconnect=true` watchdog повторяет проверку и пишет ту же ошибку циклически.

## Проверка окружения вручную (успешная)

Ручные команды в PowerShell ранее прошли:

- `wsl -d ubuntu -- /bin/bash -lc "echo ok"` -> `ok`
- `wsl -d ubuntu -- /bin/bash -lc "source ~/hermes-agent/venv/bin/activate && hermes status"` -> успешный статус Hermes

Это подтверждает, что WSL+Hermes рабочие, а проблема локализуется в значении/применении `WslDistro` внутри запущенного приложения.

## Рекомендованные действия

1. Закрыть все процессы `Hermes.Wpf`.
2. Запустить приложение заново.
3. Проверить `Settings`:
   - `WSL Distro = Ubuntu`
   - `Venv Path = ~/hermes-agent/venv`
   - `Hermes Command = hermes`
4. Нажать `Reconnect`.
5. Повторно проверить новый лог: ошибка `WSL_E_DISTRO_NOT_FOUND` должна исчезнуть.

---

## Source: `Project_Structure.md`

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

---

## Source: `Code_Context.md`

# Code Context for Connection Diagnostics

## 1) ConnectionService (`Services/ConnectionService.cs`)

Ключевые моменты:
- Preflight начинается с `wsl.exe --status`.
- Затем идут проверки `venv`, `Hermes CLI`, `hermes status`.
- Все bash-команды запускаются через `-- /bin/bash -lc` и `-d "<WslDistro>"`.

Основной фрагмент:

```csharp
private static string BuildWslArgs(HermesSettings settings, string bashCommand)
{
    var escaped = bashCommand.Replace("\"", "\\\"");
    if (!string.IsNullOrWhiteSpace(settings.WslDistro))
    {
        return $"-d \"{settings.WslDistro}\" -- /bin/bash -lc \"{escaped}\"";
    }

    return $"-- /bin/bash -lc \"{escaped}\"";
}
```

## 2) HermesService (`Services/HermesService.cs`)

Ключевые моменты:
- Chat/quick actions используют тот же надежный шаблон WSL-вызова.
- Добавляется `cd '<wslWorkDir>'` при наличии project context.

Основной фрагмент:

```csharp
private static string BuildWslArgs(HermesSettings settings, string bashCommand, string? wslWorkDir = null)
{
    var cdPrefix = string.IsNullOrWhiteSpace(wslWorkDir) ? string.Empty : $"cd '{wslWorkDir}' && ";
    var fullCmd = $"{cdPrefix}{bashCommand}";
    var escaped = fullCmd.Replace("\"", "\\\"");

    if (!string.IsNullOrWhiteSpace(settings.WslDistro))
    {
        return $"-d \"{settings.WslDistro}\" -- /bin/bash -lc \"{escaped}\"";
    }

    return $"-- /bin/bash -lc \"{escaped}\"";
}
```

## 3) MainViewModel (`ViewModels/MainViewModel.cs`)

Ключевые моменты:
- `ReconnectCommand` запускает `RefreshConnectionAsync()`.
- Watchdog таймер повторяет reconnect при `AutoReconnect=true`.
- Именно поэтому ошибка в логах повторяется циклически.

Основной фрагмент:

```csharp
_watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
_watchdogTimer.Tick += async (_, _) => await WatchdogTickAsync();
_watchdogTimer.Start();
```

## 4) Settings (`Models/HermesSettings.cs` + `Views/SettingsWindow.xaml`)

Ключевые параметры:
- `WslDistro`
- `VenvPath`
- `HermesCommand`
- `ChatTimeoutSeconds`
- `AutoReconnect`

Для текущей ошибки критичен `WslDistro`.
