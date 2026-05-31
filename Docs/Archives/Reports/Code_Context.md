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
