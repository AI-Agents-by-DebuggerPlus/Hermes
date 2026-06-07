# Формат сообщений в Supabase (`public.messages`)

Документ описывает, как строки чата хранятся в таблице Supabase и какими полями обмениваются клиенты (DesktopVoiceChat, Android и др.) через PostgREST / Realtime.

- Базовая схема: [`001_messages_table.sql`](001_messages_table.sql)
- Миграция с `recipient_name`: [`NewSupaBaseTableSchema.sql`](NewSupaBaseTableSchema.sql)

---

## Таблица

| Колонка          | Тип PostgreSQL   | Обязательность | Описание |
|------------------|------------------|----------------|----------|
| `id`             | `uuid`           | PK, по умолчанию `gen_random_uuid()` | Уникальный идентификатор сообщения. |
| `sender_id`      | `uuid`           | NOT NULL, FK → `auth.users(id)` | Пользователь из Supabase Auth (`auth.uid()` в JWT). |
| `sender_name`    | `text`           | NOT NULL       | Отображаемое имя отправителя (произвольная строка для UI). |
| `recipient_name` | `text`           | NOT NULL, default `'Unknown'` | Имя получателя сообщения (адресат в UI и при маршрутизации). |
| `content`        | `text`           | NOT NULL       | Текст сообщения (включая многострочный). |
| `created_at`     | `timestamptz`    | NOT NULL       | Время создания. В проекте задаётся **клиентом** при INSERT (см. `005_messages_created_at_client_only.sql`). |

Для уже развёрнутой БД без колонки получателя выполните:

```sql
-- NewSupaBaseTableSchema.sql
alter table public.messages
  add column recipient_name text not null default 'Unknown';
```

Старые строки получат `recipient_name = 'Unknown'`, пока клиенты не начнут передавать значение явно.

---

## Правила Row Level Security (RLS)

- **SELECT**: роль `authenticated` (политика `"read all"`).
- **INSERT**: политика `"insert own"` — `sender_id` должен совпадать с `auth.uid()`.
- **DELETE**: политика для очистки чата (см. `002_messages_delete_policy.sql`).

Клиент перед INSERT должен быть аутентифицирован (в т.ч. анонимная сессия GoTrue даёт `authenticated` и свой `auth.uid()`).

---

## Тело INSERT (PostgREST / REST)

Рекомендуемые поля при вставке:

```json
{
  "sender_id": "<uuid пользователя из JWT sub / session>",
  "sender_name": "WPF User",
  "recipient_name": "Hermes",
  "content": "Текст сообщения",
  "created_at": "2026-05-14T12:34:56.789+00:00"
}
```

- `sender_id` — UUID в JSON (строка в кавычках).
- `sender_name`, `recipient_name` — непустые `text` после `trim` на стороне клиента.
- `created_at` — ISO-8601 с часовым поясом (`Z` или смещение, например `+00:00`).

Если **`id` не передаёт**, сработает default (`gen_random_uuid()`). Если **`recipient_name` не передаёт** на старой схеме без колонки — INSERT упадёт; на актуальной схеме с default в БД подставится `'Unknown'`, но клиенты проекта всегда отправляют поле явно.

### Допустимые значения `recipient_name` в клиентах

В настройках DesktopVoiceChat и Android для исходящих сообщений выбирается одно из:

| Значение  | Назначение |
|-----------|------------|
| `Unknown` | Неизвестный / общий адресат (совпадает с default в БД). |
| `Hermes`  | Бот / ассистент Hermes. |
| `Android` | Клиент Android. |

Произвольные строки в UI клиентов не предусмотрены: при сохранении настроек значение нормализуется к списку (вне списка → `Hermes`).

---

## Поведение DesktopVoiceChat (WPF)

При вызове `SendMessageAsync(senderName, recipientName, content, clientCreatedAt)` в таблицу уходит:

| Поле             | Источник |
|------------------|----------|
| `sender_id`      | `Auth.CurrentUser.Id` |
| `sender_name`    | «Имя отправителя» в настройках |
| `recipient_name` | «Имя получателя» в настройках (выпадающий список) |
| `content`        | текст из поля ввода |
| `created_at`     | `DateTimeOffset` клиента в момент отправки |

Ответ OpenAI (если включён) записывается отдельным INSERT: `sender_name` = имя бота, `recipient_name` = имя отправителя пользователя (обратный адрес).

В списке чата строка отображается как **`sender_name → recipient_name`**.

Поле `id` в модели C# есть; при INSERT идентификатор обычно приходит из ответа PostgREST или Realtime.

---

## Соглашение для «логов» / диагностики (опционально)

DesktopVoiceChat может **сохранять в локальные файлы** сообщения, распознанные как логи, если в настройках парсинга заданы ключевые слова в **начале** поля `content` (после ведущих пробелов), например префикс `Diagnostics`.

В Supabase это обычная строка `messages` с теми же колонками, включая `recipient_name`; отличие только в `content` и при необходимости в `sender_name` / `recipient_name` для маршрутизации на диске.

Пример `content`:

```text
Diagnostics: wake engine started; mic=true
```

---

## Realtime

Таблица `public.messages` должна быть в publication `supabase_realtime` (см. конец `001_messages_table.sql`). Клиенты подписываются на **INSERT** и получают полную строку:

`id`, `sender_id`, `sender_name`, `recipient_name`, `content`, `created_at`.

---

## Android / другие клиенты

Формат тот же: одна строка в `public.messages`, те же имена колонок (snake_case в JSON). Перед INSERT:

1. Сессия Supabase установлена; `sender_id` = `sub` из JWT.
2. `sender_name`, `recipient_name`, `content` — непустые строки после `trim`.
3. `recipient_name` — одно из `Unknown`, `Hermes`, `Android` (настройки приложения).
4. `created_at` — ISO-8601, совместимый с `timestamptz`.

В списке чата подпись: **`sender_name → recipient_name`** (как в DesktopVoiceChat).

---

## Выгрузка истории (экспорт)

JSON-файл из DesktopVoiceChat («Выгрузить историю…») содержит массив объектов с полями:

`id`, `sender_id`, `sender_name`, `recipient_name`, `content`, `created_at` (snake_case, UTF-8).
