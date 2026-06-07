# Формат озвучки ru/en для Android Assistant

Документ описывает поле `content` в таблице Supabase `public.messages`, когда сообщение предназначено для **озвучки на Android** (Text-to-Speech). Это контракт между Hermes.Wpf / Hermes CLI и Android-клиентом.

См. также:

- [Формат сообщений Supabase](Формат_сообщений_Supabase.md) — схема таблицы, `sender_name`, `recipient_name`
- `Hermes.Wpf/Services/BilingualSegmentFormatter.cs` — эталонная реализация на C#
- `Hermes.InAppAssistant/AppAssistantKnowledge.cs` — правила для модели (`AndroidTtsSupabaseOutboundRu`)

---

## Когда Android озвучивает строку

Обычно озвучиваются **ответы Hermes**, адресованные Android:

| Поле             | Типичное значение |
|------------------|-------------------|
| `sender_name`    | `Hermes` (настраивается в Hermes.Wpf) |
| `recipient_name` | `Android` |
| `content`        | JSON ru/en (см. ниже) |

Android **не обязан** озвучивать:

- строки с `recipient_name = Hermes` (вход пользователя / Desktop);
- служебные payload (session voice, startup notification) — их WPF помечает и не показывает в основном чате;
- специальные JSON (`flashcard`, `skill`) — показываются в UI, озвучка по отдельным правилам приложения.

---

## Два формата `content` для TTS

### 1. Многострочный (предпочтительный для ответов агента)

**Одно предложение = одна строка = один JSON-объект.**  
Строки разделяются `\n` (LF). Пустые строки игнорируются.

```text
{"ru":"Это","en":"Hermes Command Center","ru":"программа на","en":"Windows","ru":"для работы с проектами и чатом."}
{"ru":"Сейчас открыт проект","en":"TestProject","ru":", режим ассистент через","en":"OpenRouter"}
```

Правила для **генерации** (Hermes agent / промпт):

- только JSON-строки, без markdown, без текста до/после;
- 2–4 предложения на ответ;
- без символов `-` и `—`;
- без таблиц и маркированных списков;
- `"ru"` — русский текст; `"en"` — латиница, бренды, имена (`OpenRouter`, `Windows`, `W S L`, `Supabase`);
- внутри одной строки — чередующиеся пары `"ru"` / `"en"` (сколько нужно для одного предложения).

### 2. Один JSON-объект на всё сообщение

Используется, когда WPF автоматически форматирует короткий plain-text ответ агента:

```json
{"ru":"Режим торговли, проект ","en":"TestTradingPlatform"}
```

или одноязычный фрагмент:

```json
{"ru":"Готово, проверьте терминал."}
```

```json
{"en":"agent mode, project MyProject"}
```

---

## Критично: повторяющиеся ключи `"ru"` и `"en"`

В одном JSON-объекте **допустимы и ожидаемы повторяющиеся ключи**:

```json
{"ru":"Подключён","en":"Supabase","ru":" и синхронизация активна."}
```

Это **не** стандартный JSON для большинства парсеров: `JSONObject` (Android), `Jackson`, `System.Text.Json` при доступе по ключу оставляют **только последнее** значение.

### Правильный разбор на Android

Нужен парсер, сохраняющий **порядок** пар ключ–значение:

1. Разбить `content` на строки (`\n`), trim, отбросить пустые.
2. Для каждой строки, если начинается с `{` и заканчивается `}`:
   - извлечь упорядоченный список фрагментов `(lang, text)`, где `lang ∈ {ru, en}`;
   - **игнорировать** любые другие ключи (если есть `"type"` — это не TTS, см. ниже).
3. Озвучить фрагменты **строго по порядку** внутри строки, затем перейти к следующей строке.

Псевдокод:

```kotlin
data class VoiceFragment(val lang: String, val text: String)

fun parseTtsContent(content: String): List<List<VoiceFragment>> {
    return content.lineSequence()
        .map { it.trim() }
        .filter { it.isNotEmpty() }
        .mapNotNull { line ->
            if (!line.startsWith("{") || !line.endsWith("}")) return@mapNotNull null
            parseOrderedRuEnObject(line)  // custom: regex or streaming tokenizer
        }
        .toList()
}

fun speakMessage(sentences: List<List<VoiceFragment>>) {
    for (sentence in sentences) {
        for (frag in sentence) {
            val locale = when (frag.lang) {
                "ru" -> Locale("ru")
                "en" -> Locale.US
                else -> continue
            }
            tts.speak(frag.text, locale)
            awaitSpeechDone()
        }
        // опционально: пауза между предложениями
    }
}
```

Рекомендуемый способ извлечения пар без «умного» JSON:

- regex `"ru"\s*:\s*"((?:\\.|[^"\\])*)"` и `"en"\s*:\s*"((?:\\.|[^"\\])*)"` **по порядку появления в строке**;
- или потоковый разбор: после `{` читать `"ru"` / `"en"`, затем строковое значение с учётом `\"`.

### Эталон на C# (Hermes.Wpf)

`BilingualSegmentFormatter.TryExtractSingleObjectVoicePlainText` обходит `EnumerateObject()` и собирает все `"ru"` / `"en"` подряд. Для UI-preview склеивает через пробел:

```csharp
// ru + en + ru + en → одна строка для отображения
string display = string.Join(" ", parts);
```

Для TTS на Android **склеивать не нужно** — каждый фрагмент озвучивается своей локалью.

---

## Выбор языка TTS

| Ключ  | Locale (Android) | Содержимое |
|-------|------------------|------------|
| `ru`  | `ru-RU`          | Кириллица, русская грамматика, числа рядом с русским текстом |
| `en`  | `en-US` (или `en`) | Латиница, бренды, аббревиатуры, имена файлов/проектов |

Примеры `"en"`-фрагментов: `Hermes`, `OpenRouter`, `W S L`, `Supabase`, `TestProject`, `Windows`.

Пробелы в `"en":"W S L"` — **намеренные** (побуквенное или по-сyllable чтение аббревиатуры). Не удалять.

### Паузы

- Три пробела подряд в тексте (`"   "`) — intentional pause; **не** схлопывать в один пробел перед TTS.
- Четырёх и более пробелов WPF схлопывает до одного (`BilingualSegmentFormatter`).

---

## Форматы, которые **не** являются TTS ru/en

Перед TTS-парсингом проверить «особые» payload:

| Признак в `content` | Действие Android |
|---------------------|------------------|
| `"type":"flashcard"` | Карточка EN/RU — UI flashcard, не sentence-TTS |
| `"skill":"flashcard_start"` / `flashcard_stop` | Команда навыка |
| Legacy `{"hermes_wpf":"session",...}` | Служебное, можно не озвучивать |
| Startup JSON от `AppLifecycleSupabasePayload` | Служебное уведомление о запуске WPF |
| Строка не начинается с `{` | Plain text — озвучить целиком на языке по эвристике (кириллица → ru) |
| `{}` | Пусто — пропустить |

Проверка «это sentence-TTS» (как в WPF):

- каждая непустая строка — `{...}` с `"ru"` и/или `"en"`;
- нет `"type"` и нет `"skill"` на строке.

---

## Кто формирует `content`

| Источник | Формат |
|----------|--------|
| Hermes agent (Supabase relay on) | Многострочный JSON по правилам промпта |
| Hermes.Wpf `BilingualSegmentFormatter.ToSupabaseContent` | Авто-разбиение plain text по алфавиту (кириллица → `ru`, латиница → `en`) |
| Session / startup voice | Один объект `{"en":"..."}` или `{"ru":"..."}` |

Hermes.Wpf **не перекодирует** уже готовые многострочные TTS-строки и flashcard JSON — пишет в Supabase as-is.

---

## Примеры

### Ответ агента (озвучить полностью)

`recipient_name`: `Android`

```text
{"ru":"Запрос принят","en":"Hermes","ru":" обрабатывает задачу в проекте ","en":"TestProject","ru":"."}
```

Озвучка: ru → en → ru → en → ru (5 фрагментов, одно предложение).

### Автоформат WPF из plain text

Вход агента: `Режим торговли, проект TestTradingPlatform`

В Supabase:

```json
{"ru":"Режим торговли, проект ","en":"TestTradingPlatform"}
```

### Flashcard (не sentence-TTS)

```json
{"type":"flashcard","en":"neural network","ru":"нейронная сеть"}
```

Показать карточку; озвучивать `"en"` / `"ru"` по UX flashcard-mode, **не** через sentence-парсер.

### Plain fallback

`content`: `Привет, это тест`

Озвучить как один русский utterance (нет JSON).

---

## Отображение в UI чата

Подпись строки (как в DesktopVoiceChat): **`sender_name → recipient_name`**.

Текст для превью (без TTS):

1. Если многострочный TTS — для каждой строки собрать plain text (все `ru`/`en` через пробел), строки через `\n` или пробел.
2. Если один JSON ru/en — то же склеивание.
3. Flashcard — показать `en` / `ru` в карточном UI.

---

## Чеклист реализации Android Assistant

- [ ] Подписка на Realtime INSERT в `messages` (или polling).
- [ ] Фильтр: озвучивать, если `recipient_name == "Android"` (или настройка пользователя).
- [ ] Парсер **ordered** ru/en с поддержкой duplicate keys.
- [ ] `TextToSpeech` / `Locale`: `ru-RU` и `en-US` per fragment.
- [ ] Очередь utterance: дождаться `onDone` перед следующим фрагментом.
- [ ] Исключения: flashcard, skill JSON, session/startup.
- [ ] Plain text fallback для старых строк без JSON.
- [ ] Не озвучивать собственные INSERT (echo), если `sender_id == currentUserId`.

---

## Версия контракта

| Версия | Дата       | Изменения |
|--------|------------|-----------|
| 1.0    | 2026-06-03 | Первый документ: multi-line sentence JSON, duplicate keys, TTS locale rules |

При изменении формата обновляйте этот файл и `BilingualSegmentFormatter.cs` синхронно.
