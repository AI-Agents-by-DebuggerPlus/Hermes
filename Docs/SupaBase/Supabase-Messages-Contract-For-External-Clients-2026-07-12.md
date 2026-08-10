# Контракт Supabase `public.messages` — справка для внешнего клиента

**Дата:** 2026-07-12  
**Проект:** TaskerToWpf (WpfChat + AndroidChat)  
**Назначение:** описание актуальной таблицы, форматов `content` и правил доступа, чтобы другой клиент (общий проект Supabase) не ломался из‑за расхождений или недавних расширений протокола.

---

## 1. Общая схема

```
Внешний клиент ──► public.messages ◄── WpfChat (PC)
                         ▲
                         │
                   AndroidChat / Tasker (логи и чат)
```

Все чат-сообщения и служебные логи идут в **одну** таблицу `public.messages`.  
Отдельная таблица `md_documents` и bucket Storage `chat-files` — смежные сервисы; основной wire-протокол чата — строки в `messages`.

| Компонент этого репо | Роль |
|----------------------|------|
| WpfChat | INSERT/SELECT/DELETE, Realtime SDK, TTS JSON, файлы |
| AndroidChat | INSERT/SELECT, WebSocket Realtime, мост Tasker |
| Tasker | Не ходит в Supabase напрямую → Broadcast → AndroidChat |

---

## 2. Таблица `public.messages` (актуальная модель)

Модель в коде (WpfChat / AndroidChat) — **ровно эти колонки**:

| Колонка | Тип (ожидаемый) | Обязательность | Описание |
|---------|-----------------|----------------|----------|
| `id` | `uuid` | PK, сервер | Не задавать при INSERT (генерирует БД) |
| `sender_id` | `uuid` / text uuid | да | Должен совпадать с `auth.uid()` сессии |
| `sender_name` | `text` | да | Логическое имя клиента (см. §4) |
| `recipient_name` | `text` | да | Логический получатель (см. §4) |
| `content` | `text` | да | Полезная нагрузка (чат / LOG / TTS JSON / file JSON) |
| `created_at` | `timestamptz` | да | Клиент обычно шлёт ISO-8601; сервер может иметь default `now()` |

**Важно для внешнего клиента:**

- Не полагайтесь на дополнительные колонки (`type`, `meta`, `payload`, …) — **их нет** в текущем контракте TaskerToWpf.
- Весь «тип» сообщения кодируется **внутри `content`** (префикс / JSON), а не отдельным полем.
- `id` после INSERT нужно читать из ответа (realtime тоже отдаёт полный row).

Код-модели:

- WpfChat: `WpfChat/Models/SupabaseMessageRow.cs`
- AndroidChat: `AndroidChat/.../data/ChatMessage.kt`, `MessageInsert` в `Models.kt`

---

## 3. Auth и RLS (правила взаимодействия)

### 3.1. Аутентификация

| Параметр | Значение в TaskerToWpf |
|----------|-------------------------|
| Key | **anon** (publishable), не service_role |
| Режим | **Anonymous Auth** (GoTrue `signInAnonymously`) |
| Dashboard | Authentication → Providers → **Anonymous** = ON |

Каждый клиент получает свой `auth.uid()` на сессию.  
`sender_id` при INSERT = этот uid.

### 3.2. Типовые политики RLS (как задумано в проекте)

| Операция | Кто | Условие |
|----------|-----|---------|
| **SELECT** | `anon` / `authenticated` | Обычно **все** строки (нужно для истории и Realtime) |
| **INSERT** | `authenticated` | `sender_id = auth.uid()` |
| **DELETE** | `authenticated` (для «Очистить чат» в WpfChat) | Политика DELETE должна разрешать очистку; иначе wipe падает |

Если внешний клиент:

- вставляет **без** сессии / с другим `sender_id` → INSERT rejected;
- ожидает **фильтр по recipient** на уровне RLS, а политики открыты на SELECT всех → увидит чужие логи/файловые JSON;
- использует только `anon` key **без** anonymous sign-in → Realtime/INSERT могут вести себя иначе (403 WebSocket, JWT errors).

### 3.3. Realtime

- Таблица должна быть в публикации **`supabase_realtime`** (Dashboard → Database → Replication).
- Подписка: **INSERT** на `public.messages`.
- AndroidChat: WebSocket `wss://…/realtime/v1/websocket?apikey={anon}&vsn=1.0.0`, topic `realtime:public:messages`.
- WpfChat: Supabase C# SDK + fallback **poll** (~10 с), если WebSocket 403/молчит.

Внешний клиент должен уметь жить и с realtime, и с периодическим SELECT (как WpfChat).

---

## 4. Имена `sender_name` / `recipient_name`

Это **не** auth-пользователи, а произвольные строки для UI и маршрутизации.

| Клиент | Типичный `sender_name` | Типичный `recipient_name` |
|--------|------------------------|---------------------------|
| WpfChat | `WpfChat` | `Android` |
| AndroidChat (чат) | `AndroidChat` | `WpfChat` |
| AndroidChat → Hermes.Wpf (общий) | `AndroidChat` / `Android` | `Hermes` |
| AndroidChat → проект Hermes | `AndroidChat` / `Android` | `Hermes.<ИмяПроекта>` (например `Hermes.Utilities`) |
| Логи → RemoteTerminal.Xp | `Hermes` / `WpfChat` | `RemoteTerminal` (`[LOG:…]`) |
| Логи Tasker через AndroidChat | `Tasker` | `WpfChat` |
| Логи AndroidChat | `AndroidChat` | `WpfChat` |

**Маршрутизация в Hermes.Wpf:** при `recipient_name = Hermes.<Проект>` приложение выбирает проект в Project Manager с тем же именем папки, загружает его историю и принимает сообщение в этот чат (при необходимости добавляет папку из `HermesProjects`, если она есть на диске, но ещё не в списке).

**Риски для внешнего клиента:**

| Если внешний клиент… | Эффект |
|----------------------|--------|
| Шлёт на `recipient_name=AndroidChat` | WpfChat всё равно покажет (SELECT все); Android может не считать «своим» по имени |
| Использует другие имена (`PC`, `Phone`) | Сообщения в таблице есть, но фильтры UI/логов WpfChat/AndroidChat могут их игнорировать или показать «не туда» |
| Ставит `sender_name=Tasker` для обычного чата | WpfChat может отнести строку к лог-файлам Tasker, если `content` ещё и `[LOG:…]` |

Рекомендация: согласовать **фиксированный словарь имён** с владельцем TaskerToWpf или читать оба направления без жёсткого фильтра по имени.

---

## 5. Форматы поля `content` (версия протокола)

Один столбец — **несколько взаимоисключающих форматов**. Клиенты TaskerToWpf разбирают так:

### 5.1. Обычный текст чата

```
произвольная UTF-8 строка
```

Примеры: `привет`, `1 2 3 4 5`, `тест из Tasker`.

- Показывается в пузырях чата.
- Не начинается с `[LOG:` и не является file/TTS JSON (см. ниже).

### 5.2. Служебный лог — `[LOG:…]`

```
[LOG:{category}] {message}
```

Примеры:

```
[LOG:VoiceLoop] Get Voice done text=53 raw=53 conf=89
[LOG:Tasker] Headset profile ON from AndroidChat
[LOG:App] Application started — v1.0.2 …
```

| Клиент | Поведение |
|--------|-----------|
| AndroidChat | **Не** показывает в списке чата (фильтр) |
| WpfChat | Пишет в файлы логов / панель; **не** как обычный пузырь чата (парсер `LogMessageParser`) |

**Ломающий риск:** если внешний клиент шлёт пользовательский текст, начинающийся с `[LOG:`, TaskerToWpf спрячет его из чата.

### 5.3. TTS JSON (ответы Hermes.Wpf → AndroidChat)

Канон:

- `Docs/SupaBase/Формат_TTS_Android_Assistant.md` (v1.2)
- `Docs/SupaBase/AndroidChat-Incoming-TTS-Protocol.md` (AndroidChat ≥ 1.0.41)

Агент отвечает блоками в stdout; Hermes.Wpf в `messages.content` кладёт TTS с оболочкой **`[Voice]…[/Voice]`**:

- `[info]` — текст для чтения (в UI чата WPF / Android);
- `[Voice]…[/Voice]` — многострочный TTS JSON (объекты с `ru`/`en`); без Voice озвучки нет.

WPF при публикации оборачивает legacy JSON / `[speak]` в `[Voice]` автоматически.

AndroidChat в UI разворачивает TTS через `MessageDisplayFormatter` / `toSpeakPlan`.  
Автоозвучка входящих зависит от версии AndroidChat и наличия Voice.

**Ломающий риск:** внешний клиент, ожидающий «просто текст» в `content`, получит Voice+JSON; сырой текст без Voice — в чате виден, но не озвучивается (≥ 1.0.41).

### 5.4. Файловое сообщение (Storage + JSON в `content`)

Бинарник **не** кладётся в `content`. Схема:

1. Upload в Storage bucket **`chat-files`** (private), путь вида `{auth.uid()}/{uuid}/{filename}`.
2. INSERT в `messages` с JSON:

```json
{
  "type": "file",
  "name": "backup2July_fixed_v11.xml",
  "bucket": "chat-files",
  "path": "{user_id}/{folder}/{filename}",
  "mime": "application/xml",
  "size": 45261
}
```

| Поле | Обязательно |
|------|-------------|
| `type` | да, равно `"file"` |
| `name`, `path` | да |
| `bucket` | да (`chat-files`) |
| `mime`, `size` | желательно |

Миграция: `Docs/Supabase/migrations/002_chat_files_storage.sql`.

**Ломающий риск:** клиент без поддержки `type=file` покажет сырой JSON в чате или упадёт на парсере; клиент без Storage policies не скачает файл.

### 5.5. Приоритет разбора (как в TaskerToWpf)

Практический порядок для совместимого клиента:

1. Если `content` trim начинается с `[LOG:` → служебный лог.  
2. Иначе если JSON с `"type":"file"` → файловый метаданные.  
3. Иначе если JSON с ключами `ru`/`en` (и без `type`/`skill`) → TTS.  
4. Иначе → обычный текст чата.

`ignoreUnknownKeys` при JSON — желателен.

---

## 6. REST / Realtime — минимальный API

### INSERT (чат)

```http
POST /rest/v1/messages
apikey: {anon}
Authorization: Bearer {session_access_token}
Content-Type: application/json
Prefer: return=representation

{
  "sender_id": "{auth.uid}",
  "sender_name": "YourClient",
  "recipient_name": "WpfChat",
  "content": "hello",
  "created_at": "2026-07-12T18:00:00+00:00"
}
```

### SELECT (история)

```http
GET /rest/v1/messages?order=created_at.asc
Authorization: Bearer {session_access_token}
```

Фильтры по `recipient_name` — опциональны; TaskerToWpf часто грузит **все** строки и фильтрует на клиенте (`[LOG:…]`).

### Realtime

Подписка на INSERT; payload содержит `record` со всеми колонками §2.

---

## 7. Storage (если внешний клиент шлёт файлы)

| Параметр | Значение |
|----------|----------|
| Bucket | `chat-files` |
| Public | **false** |
| Лимит | 50 MB (в миграции) |
| INSERT path | первый сегмент = `auth.uid()` |
| SELECT | любой `authenticated` |

Без anonymous session upload/download не совпадут с политиками.

---

## 8. Что изменилось / где обычно ломается чужой клиент

Сводка рисков относительно «старого» чата только с plain-text:

| Изменение в экосистеме TaskerToWpf | Симптом у внешнего клиента |
|------------------------------------|----------------------------|
| Массовые строки `[LOG:…]` в той же таблице | «Спам» в чате, если нет фильтра логов |
| TTS JSON от WpfChat | В UI виден JSON вместо текста |
| File JSON + Storage | Непонятные пузыри / битые ссылки |
| Anonymous auth обязателен для INSERT | Старый клиент с одним anon без sign-in → 401 |
| Realtime 403 / нужен poll | «Сообщения приходят с задержкой» |
| DELETE wipe из WpfChat | Внешний клиент теряет историю без warning |
| Разные `sender_name` / `recipient_name` | Сообщения есть в БД, UI «пустой» из‑за клиентского фильтра |
| Числовой `content` (`"53"`) | Не проблема таблицы; у AndroidChat был баг Intent Int→String (исправлен в приложении, не в Supabase) |

**Таблица колонок сама по себе стабильна** (шесть полей).  
Ломает совместимость в основном **семантика `content`** и **общий «мусорный» поток логов** в той же таблице.

---

## 9. Рекомендации внешнему клиенту

1. **Читать** все INSERT, но классифицировать `content` по §5.5.  
2. **Не** отображать `[LOG:…]` как пользовательский чат (или вынести в отдельную панель).  
3. При отправке использовать согласованные `sender_name` / `recipient_name` или документировать свои.  
4. Всегда: anonymous (или иной) JWT + `sender_id = auth.uid()`.  
5. Не добавлять колонки в `messages` без согласования — оба клиента TaskerToWpf их не мапят.  
6. Для файлов — только контракт §5.4 + bucket `chat-files`.  
7. Заложить fallback poll, если Realtime нестабилен.  
8. Перед продом — тестовый INSERT plain / LOG / TTS / file и проверка в WpfChat + AndroidChat.

---

## 10. Ссылки в репозитории

| Документ / код | Содержание |
|----------------|------------|
| `Docs/README.md` | Обзор схемы `messages` |
| `Docs/WpfChat.md` | Клиент PC, TTS, логи |
| `Docs/AndroidChat.md` | Клиент Android, WebSocket |
| `Docs/Supabase-File-Transfer.md` | Storage + file JSON |
| `Docs/Supabase/migrations/002_chat_files_storage.sql` | Bucket policies |
| `WpfChat/Models/SupabaseMessageRow.cs` | ORM-модель |
| `WpfChat/Services/LogMessageParser.cs` | `[LOG:…]` |
| `WpfChat/Services/TtsMessageFormatter.cs` | TTS JSON |
| `WpfChat/Services/FileMessageFormat.cs` | file JSON |
| `AndroidChat/.../LogMessageFormat.kt` | Сборка/детект логов |
| `AndroidChat/.../FileMessageFormat.kt` | file JSON |

---

## 11. Версия контракта (зафиксировать для согласования)

| Поле | Значение |
|------|----------|
| Контракт таблицы | `messages` v1 — 6 колонок (§2) |
| Версия `content`-протокола | v1.1 — plain + `[LOG:]` + TTS JSON + `type=file` |
| Auth | Anonymous + anon key |
| Realtime | INSERT `public.messages` |
| Дата снимка | 2026-07-12 |

При изменении колонок или форматов `content` обновлять этот файл и уведомлять владельцев всех клиентов одной БД.
