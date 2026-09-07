# Cursor Agent Prompt — Android Photo Parser (Hermes)

Скопируй **весь блок ниже** (от `---BEGIN PROMPT---` до `---END PROMPT---`) в новое окно Cursor Agent как задачу на реализацию.

---

## ---BEGIN PROMPT---

### Цель

Создай **новое Android-приложение** (отдельный модуль/проект в репозитории Hermes или рядом), которое:

1. Делает **фото камерой** (paystub, чек, документ).
2. Извлекает **текст on-device** через **Google ML Kit Text Recognition**.
3. **Сохраняет локально** исходное фото и файл с распознанным текстом в **отдельной папке приложения**, с возможностью **найти и открыть** их позже в UI приложения.
4. *(Опционально, но желательно)* отправляет распознанный текст в **Supabase** (`public.messages`) для приёма в **Hermes.Wpf** (relay).

Приложение **не** парсит share-link Google Photos и **не** использует browser scraping.

---

### Контекст экосистемы Hermes

- Desktop: **Hermes.Wpf** опрашивает Supabase и показывает входящие строки в чате (`recipient_name = Hermes`).
- Документация Supabase: `D:\Programming\AI_Agents\Hermes\Docs\SupaBase\Формат_сообщений_Supabase.md`
- Схема таблицы: `D:\Programming\AI_Agents\Hermes\Docs\SupaBase\NewSupaBaseTableSchema.sql`
- Use-case: сверка рабочих часов (paystub OCR → Hermes → Excel `Working_days_2026_...xlsx` на ПК).

---

### Технологический стек (обязательно)

| Область | Выбор |
|--------|--------|
| Язык | **Kotlin** |
| UI | **Jetpack Compose** (Material 3) |
| Min SDK | **26** (или обоснуй если ниже) |
| Камера | **CameraX** |
| OCR | **Google ML Kit Text Recognition** — `com.google.mlkit:text-recognition` (Latin; при необходимости `text-recognition-chinese` не нужен) |
| Локальное хранилище | app-specific storage (`context.getExternalFilesDir` или `filesDir/scans/`) |
| Список сканов | Room **или** JSON-индекс + File API (предпочти Room для поиска/сортировки) |
| Сеть (опционально) | Supabase Kotlin SDK, anonymous auth |

**Запрещено** для OCR: Tesseract, cloud-only OCR без offline fallback, WebView для Google Photos.

---

### Локальное хранение (обязательно)

Структура каталога (пример):

```
{appFiles}/scans/
  {scanId}/                    # scanId = UUID или yyyyMMdd_HHmmss
    photo.jpg                  # исходное фото (JPEG, quality ~85)
    text.txt                   # сырой текст ML Kit (UTF-8)
    meta.json                  # метаданные (см. ниже)
```

**meta.json** (минимум):

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "createdAt": "2026-06-08T12:34:56-07:00",
  "photoFile": "photo.jpg",
  "textFile": "text.txt",
  "ocrEngine": "mlkit-text-recognition",
  "ocrLanguage": "latin",
  "charCount": 1234,
  "lineCount": 42,
  "supabaseSent": false,
  "supabaseMessageId": null,
  "note": ""
}
```

**Требования:**

- Каждый скан — **отдельная подпапка**; фото и текст **всегда парой**.
- После OCR показать экран **превью**: фото + редактируемый текст ( пользователь может исправить ошибки OCR перед сохранением).
- Экран **«История / Архив»**: список сканов (дата, превью-миниатюра, первые ~80 символов текста, статус отправки в Supabase).
- По тапу — детальный экран: полное фото (zoom), полный текст, кнопки «Поделиться», «Отправить в Hermes», «Удалить».
- Поиск по истории: минимум фильтр по дате + поиск substring в сохранённом `text.txt` (in-memory или Room FTS — на твоё усмотрение).

---

### OCR pipeline (ML Kit)

1. CameraX capture → `ImageProxy` / `Bitmap`.
2. `TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)` (или `TextRecognizerOptions.Builder` с Latin).
3. `process(inputImage).await()` → собрать `Text.text` или блоки с сохранением переносов строк.
4. Обработка ошибок: нет текста, ML Kit unavailable, camera permission denied — понятные сообщения в UI.
5. **Не** делать structured parsing paystub на Android в v1 — только сырой OCR + ручная правка. Структурирование — на Hermes CLI.

---

### Supabase (опционально, Phase 1.5 или вместе с v1)

**Настройки** (EncryptedSharedPreferences или DataStore):

- Supabase URL
- Anon key
- `sender_name` (default: `Phone`)
- `recipient_name` (default: `Hermes`)
- Toggle «Отправлять в Hermes автоматически после сохранения»

**INSERT** в `public.messages`:

```json
{
  "sender_id": "<auth.uid()>",
  "sender_name": "Phone",
  "recipient_name": "Hermes",
  "content": "{\"type\":\"document_ocr\",\"scanId\":\"...\",\"text\":\"...\",\"capturedAt\":\"...\"}",
  "created_at": "<ISO-8601 client time>"
}
```

- Перед INSERT: anonymous sign-in (`SignInAnonymously`).
- `content`: JSON-строка с полем `text` (экранировать кавычки). Лимит: если текст > ~50 KB, обрезать с пометкой `[truncated]` в JSON.
- После успеха: обновить `meta.json` (`supabaseSent: true`, `supabaseMessageId`).
- Ошибки сети: скан **всё равно сохранён локально**; показать «Не отправлено — повторить».

Ссылки: `Docs/SupaBase/Формат_сообщений_Supabase.md`

---

### UI экраны (минимум)

1. **Home** — кнопка «Сканировать», кнопка «Архив», иконка настроек.
2. **Camera** — превью CameraX, shutter, переключение камеры (если просто).
3. **Review** — фото + EditText/Multi-line поле с OCR-текстом, «Сохранить», «Переснять».
4. **Archive** — LazyColumn сканов, pull-to-refresh, search bar.
5. **Scan detail** — photo viewer + text + actions.
6. **Settings** — Supabase credentials, toggles, about.

Тёмная тема не обязательна в v1; поддержи system theme.

---

### Разрешения Android

- `CAMERA` — runtime request с rationale.
- Для Android 13+: при сохранении в app-specific dir **не** нужен `READ_MEDIA_IMAGES` для своих файлов.
- Не пиши в общую галерею без явного action «Экспорт в галерею».

---

### Структура проекта (предложение)

```
Hermes.AndroidPhotoParser/          # новый Gradle module или отдельная папка в repo
  app/
    src/main/java/.../
      camera/
      ocr/          # MlKitOcrService
      storage/      # ScanRepository, ScanStorage
      supabase/     # SupabaseSender (optional)
      ui/
    src/main/res/
  build.gradle.kts
  settings.gradle.kts
```

Если в монорепо Hermes нет Android — создай **standalone** проект в  
`D:\Programming\AI_Agents\Hermes\Hermes.AndroidPhotoParser\`  
и README с инструкцией сборки.

---

### Acceptance criteria

- [ ] Сфотографировать документ → ML Kit вернул текст → пользователь сохранил → в `{appFiles}/scans/{id}/` есть `photo.jpg`, `text.txt`, `meta.json`.
- [ ] В «Архиве» виден скан; открывается фото и текст.
- [ ] Поиск по substring находит старый скан.
- [ ] Перезапуск приложения — история на месте.
- [ ] OCR работает **без интернета** (локальное сохранение).
- [ ] *(если Supabase включён)* INSERT доходит до БД; Hermes.Wpf с relay показывает строку в чате.
- [ ] Unit-тест: парсинг/сборка `meta.json`; instrumented test не обязателен, но приветствуется для OCR mock.

---

### Вне scope (не делать в v1)

- Парсинг Google Photos share-link
- Structured paystub field extraction (regex/LLM) на телефоне
- iOS
- Синхронизация архива между устройствами
- WordPress / gallery upload

---

### Качество и стиль

- Kotlin idiomatic, coroutines + Flow где уместно.
- Не over-engineer: один `ScanRepository`, один `MlKitOcrService`.
- README на русском: сборка, permissions, структура папок, Supabase setup, скриншоты placeholder.
- Коммит только если пользователь попросит.

---

### Первый шаг для агента

1. Создай skeleton Android project (Compose + CameraX + ML Kit dependency).
2. Реализуй capture → OCR → save → archive list → detail.
3. Добавь Supabase sender за feature flag.
4. README с примером `content` для Hermes.Wpf.

---END PROMPT---

---

## Файлы для справки агенту

| Файл | Назначение |
|------|------------|
| `Docs/SupaBase/Формат_сообщений_Supabase.md` | контракт INSERT |
| `Docs/SupaBase/NewSupaBaseTableSchema.sql` | колонки `messages` |
| `Docs/Instructions/Future_Roadmap.md` | Android + Supabase в roadmap Hermes |
| `Hermes.Wpf/Services/SupabaseChatRelayService.cs` | как Desktop читает таблицу |

## Имя папки

`AndroidPhotoParcer` — намеренное написание пользователя; новый код может использовать `PhotoParser` в package name.
