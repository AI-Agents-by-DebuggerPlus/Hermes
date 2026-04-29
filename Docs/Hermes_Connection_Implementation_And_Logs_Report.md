# Отчет: реализация подключения Hermes + последние логи

## 1. Что реализовано по подключению в `Hermes.Wpf`

### Архитектура подключения
- Подключение вынесено в сервисы: `HermesService`, `ConnectionService`, `SettingsService`.
- Используется async-выполнение WSL-команд через `ProcessStartInfo` без блокировки UI-потока.
- Добавлен `StatusIndicator` с состояниями подключения:
  - `Disconnected`
  - `Checking`
  - `Connected`
  - `Error`

### Preflight и watchdog
- Реализованы preflight-проверки:
  1) доступность WSL,
  2) наличие venv,
  3) доступность Hermes CLI,
  4) выполнение `hermes status`.
- Реализован watchdog авто-переподключения (по таймеру), если включен `AutoReconnect`.

### Настройки подключения
- Настройки сохраняются в `%APPDATA%\HermesWpf\settings.json`.
- Поддерживаются параметры:
  - `WslDistro`
  - `VenvPath`
  - `HermesCommand`
  - `ChatTimeoutSeconds`
  - `AutoReconnect`
- Все WSL-запуски переведены на явный дистрибутив:
  - `wsl -d "<WslDistro>" -e bash -lc "..."`
  чтобы не зависеть от default distro (например, `docker-desktop`).

### UI-модули для подключения
- `SetupWizardWindow`:
  - preflight,
  - запуск установки Hermes из UI.
- `SettingsWindow`:
  - редактирование параметров подключения.
- Кнопка `Reconnect` в индикаторе статуса.

### Журналирование
- Логи подключения пишутся:
  - в UI-окно `Logs`,
  - в файлы `Docs/Logs/hermes_session_*.log`.
- Включена ротация: хранятся только 3 последних лог-файла.

---

## 2. Последние логи, относящиеся к подключению

Источник: `Docs/Logs/hermes_session_20260429_130128.log`

```text
2026-04-29 13:01:30.392 [ERROR] [connection] WSL failed: <3>WSL (2145 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:01:54.441 [ERROR] [connection] WSL failed: <3>WSL (2148 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:02:19.341 [ERROR] [connection] WSL failed: <3>WSL (2151 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:02:44.322 [ERROR] [connection] WSL failed: <3>WSL (2154 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:03:09.781 [ERROR] [connection] WSL failed: <3>WSL (2157 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:03:34.382 [ERROR] [connection] WSL failed: <3>WSL (2160 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:04:00.851 [ERROR] [connection] WSL failed: <3>WSL (2169 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:04:24.916 [ERROR] [connection] WSL failed: <3>WSL (2172 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:04:25.377 [ERROR] [connection] WSL failed: <3>WSL (2175 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:04:50.220 [ERROR] [connection] WSL failed: <3>WSL (2178 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:05:15.538 [ERROR] [connection] WSL failed: <3>WSL (2181 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:05:40.601 [ERROR] [connection] WSL failed: <3>WSL (2184 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:06:05.408 [ERROR] [connection] WSL failed: <3>WSL (2187 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:06:30.296 [ERROR] [connection] WSL failed: <3>WSL (2190 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:06:56.368 [ERROR] [connection] WSL failed: <3>WSL (2193 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
2026-04-29 13:07:21.024 [ERROR] [connection] WSL failed: <3>WSL (2196 - Relay) ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
```

Дополнительный последний файл: `Docs/Logs/hermes_session_20260429_124006.log`  
Содержит только старт сессии и не содержит ошибок подключения.
