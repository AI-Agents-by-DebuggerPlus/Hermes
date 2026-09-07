# RemoteTerminal — мост Supabase (просмотр)

**Дата:** 2026-08-07  

## Цель

Удалённый терминал только для просмотра `public.messages` (логи / чат на `RemoteTerminal`).

## Архитектура

```
Hermes.Wpf / DesktopVoiceChat / Android ──INSERT──► public.messages
                                                      │
                                                      ▼
                              Hermes.RemoteTerminal (.NET 8)
                              Realtime WebSocket only (no REST poll)
```

| Клиент | Канал |
|--------|--------|
| `Hermes.RemoteTerminal` | **WebSocket Realtime** — live-канал; опционально **REST backup** (XP-канал, явно включить) |
| `Hermes.RemoteTerminal.Xp` | Отдельный клиент для старых ОС (REST poll + curl) |

## Имена

| Direction | sender | recipient | content |
|-----------|--------|-----------|---------|
| Hermes.Wpf logs | `Hermes` | `RemoteTerminal` | `[LOG:HermesWpf] …` |
| DesktopVoiceChat logs | `WpfChat` | `RemoteTerminal` | `[LOG:DesktopVoiceChat] …` |
| RemoteTerminal self | `RemoteTerminal` | `RemoteTerminal` | `[LOG:RemoteTerminal] …` |

## HermesWpfTerminal в RemoteTerminal

Панели на главном окне:
1. **Счёт** — balance / equity / margin…
2. **Тикер / цена** — symbol, Bid, Ask, Lot, market status, Real/Auto
3. **Открытые позиции** + **Отложенные ордера** (если HWT отдаёт `pending_orders` / `pending`; иначе эвристика по строкам)

**Связь HWT ↔ HRT — только Supabase** (локальный `status.json` HRT не читает).

Протокол (для desktop и мобильного HRT): [`HWT_HRT_Supabase_Protocol.md`](HWT_HRT_Supabase_Protocol.md)

- `{"type":"hwt_status",…}` — публикует Hermes.Wpf **только** по команде `refresh`
- `{"type":"hwt_screenshot",…}` (+ Storage `chat-files` или base64) → WebSocket → fullscreen 10 с → погашение
- `{"type":"hwt_screenshot_repeat",…}` — повтор из кэша, без второго upload
- **Poll/F5** — разовый SELECT по запросу


**Оформление** (Настройки): схемы DarkBinance / DarkBlue / Light / HighContrast / Custom, размеры шрифтов по блокам, цвета `#RRGGBB`. Всё в `settings.json` **рядом с EXE**.

Окно **Лог** → кнопка **Отправить в Supabase** (`[LOG:RemoteTerminal]`).
