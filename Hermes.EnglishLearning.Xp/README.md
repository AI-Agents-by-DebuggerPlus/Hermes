# Hermes English Learning XP

Лёгкий клиент уроков для **Windows XP SP3** (и Win7+): только текст + удалённая навигация через Supabase. **Без озвучки.**

## Требования XP

- .NET Framework **4.0** Client/Full
- Для HTTPS к Supabase: **TLS 1.2** (Microsoft Easy Fix / Hotfix KB3140245 на XP SP3)

## Доставка урока

`recipient_name = EnglishLearning`

```json
{"type":"english_lesson","title":"…","markdown":"…"}
```

Формат MD — как у основного EnglishLearning (`Docs/EnglishLearning/CARD_MD_FORMAT.md`).

## Команды навигации (AndroidChat → XP)

`recipient_name = EnglishLearning`, `content`:

| Команда | JSON | Альтернативы |
|---------|------|----------------|
| Full screen | `{"type":"english_nav","command":"fullscreen"}` | `[NAV:fullscreen]` |
| Next | `{"type":"english_nav","command":"next"}` | `[NAV:next]` |
| Previous | `{"type":"english_nav","command":"previous"}` | `[NAV:prev]` |
| Exit | `{"type":"english_nav","command":"exit"}` | `[NAV:exit]` |

Синонимы: `full_screen`, `prev`, `close`, `quit`.

Клиент **опрашивает** REST `messages` каждые N сек (Realtime WebSocket на XP не используется).

## UI

- **Ctrl + / Ctrl −** — масштаб текста (сохраняется в `settings.json` → `UiScale`)
- **Ctrl + 0** — сброс масштаба 1.0
- **Log** / Ctrl+L — окно лога; файл в `logs\` рядом с EXE
- **Net** — диагностика интернета / TLS / Supabase (пишет в лог)

## Сборка

```bat
dotnet build Hermes.EnglishLearning.Xp\Hermes.EnglishLearning.Xp.csproj -c Release
```

EXE: `bin\Release\net40\Hermes.EnglishLearning.Xp.exe`  
Скопируйте папку `net40` на Vaio/XP вместе с `settings.json`.
