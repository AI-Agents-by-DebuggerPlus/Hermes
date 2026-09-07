# Hermes Remote Terminal



## Роли



| Приложение | TFM | Роль |

|------------|-----|------|

| **Hermes.RemoteTerminal** | **net8.0-windows** | **Актуален: v1.2.0** — основной view-only мост |

| Hermes.RemoteTerminal.Xp | net40 | Отдельный клиент для старых ОС (curl/OpenSSL) |



Оба читают `public.messages` с фильтром `recipient_name=RemoteTerminal` (настраивается).



## Основной (.NET 8)



```bash

dotnet build Hermes.RemoteTerminal\Hermes.RemoteTerminal.csproj -c Release

# EXE: Hermes.RemoteTerminal\bin\Release\net8.0-windows\Hermes.RemoteTerminal.exe

```



- **Live-лента:** WebSocket Realtime (единственный live-канал по умолчанию).

- **HWT:** только Supabase (`hwt_status` / `hwt_screenshot`). Протокол: `Docs/Reports/RemoteTerminal/HWT_HRT_Supabase_Protocol.md`

- **REST резерв (XP-канал):** опционально в Настройках — REST poll через тот же транспорт, что `RemoteTerminal.Xp` (HttpClient + `tools\curl.exe`). Включается **явно**; активируется только когда WebSocket offline ≥15 с.

- **Poll/F5:** разовый SELECT по запросу (не таймер).

- **tools/** — bundled curl для TLS-fallback (как у Xp).



## RemoteTerminal.Xp



Отдельный WinForms-клиент для XP / слабого TLS. RemoteTerminal (.NET 8) теперь имеет встроенный REST backup на том же канале — Xp остаётся для машин без .NET 8.



## Источники строк



Hermes.Wpf / DesktopVoiceChat могут зеркалировать логи: `[LOG:…]` → `recipient_name=RemoteTerminal`.



Подробнее: `Docs/Reports/RemoteTerminal/README.md`

