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
