# Remote navigation: AndroidChat → EnglishLearning

**Дата:** 2026-07-24 (проверено)  
**AndroidChat:** ≥ **1.0.10**  
**Получатель:** `recipient_name = EnglishLearning`  
**Клиенты:** `Hermes.EnglishLearning.Xp` (и при желании основной EnglishLearning)

Сообщения вставляются в `public.messages` (как обычный чат).

## JSON (предпочтительно)

```json
{"type":"english_nav","command":"fullscreen"}
{"type":"english_nav","command":"next"}
{"type":"english_nav","command":"previous"}
{"type":"english_nav","command":"exit"}
```

| `command` | Действие |
|-----------|----------|
| `fullscreen` | Переключить полный экран |
| `next` | Следующий экран карточек |
| `previous` / `prev` | Предыдущий экран |
| `exit` / `close` | Закрыть приложение |

## Короткие формы (Tasker / AndroidChat)

```
[NAV:fullscreen]
[NAV:next]
[NAV:prev]
[NAV:exit]
```

Или одна строка: `next`, `prev`, `fullscreen`, `exit`.

## AndroidChat: пустой голос / NOT HEARD → next

В профиле VoiceLoop при «не услышал» Tasker делает **LOG `NOT HEARD…` и Stop** — **без** `SEND_CHAT`.

AndroidChat (**1.0.10+**):

1. На LOG с `NOT HEARD` (или «Не услышал»), если получатель **`EnglishLearning`** → шлёт `next`.
2. На пустой `SEND_CHAT` или msg = незаполненная переменная (`%VOICE_TEXT`, `%gv_heard1`, …) при том же получателе → тоже `next`.
3. В логах пишет `recipient_name=…` и `Chat (tasker-nav) → EnglishLearning: next`.

Для других получателей `next` не формируется.

**Проверка 2026-07-24:** `AndroidChat → EnglishLearning | next` в realtime; в androidchat.log — `NOT HEARD (LOG) → next → EnglishLearning`.

## Обратный канал: страница → озвучка AndroidChat

При **загрузке урока** (старт / Open MD / урок из Supabase) INSERT:

```text
{"type":"english_lesson_meta","total_cards":42,"total_screens":14,"title":"…"}
```

| Поле | Смысл |
|------|--------|
| `total_cards` | Число карточек во всём уроке |
| `total_screens` | Число экранов (страниц) |
| `type` | `english_lesson_meta` — не озвучивать как TTS |

При смене экрана (и сразу после meta) INSERT bilingual TTS:

```text
{"en":"heart","ru":"сердце"}
{"en":"voice","ru":"голос","last":true}
```

На **последней карточке последнего экрана** добавляется `"last":true` (озвучка `en`/`ru` без изменений; ключ `last` для UI/логики Android).

| Поле | Значение |
|------|----------|
| `sender_name` | `EnglishLearning` (`TtsSenderName`) |
| `recipient_name` | `AndroidChat` (`TtsRecipientName`) |
| `content` | meta JSON **или** bilingual TTS (по одной строке JSON на карточку) |

## Урок (без изменений)

```json
{"type":"english_lesson","title":"Title","markdown":"---\n…"}
```
