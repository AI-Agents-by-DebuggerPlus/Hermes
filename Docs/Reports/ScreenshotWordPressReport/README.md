# Скриншот рабочего стола и отправка на WordPress

**Дата:** 2026-06-11  
**Код:** `Hermes.Wpf`, `Hermes.WpGallery`, `Hermes.WpGallery.Tool`, плагин `hermes-image-receiver`

---

## 1. Область отчёта

Как в экосистеме Hermes **создаётся** скриншот и **отправляется** на сайт WordPress.  
**Акцент:** в каком виде (формат HTTP, JSON, кодировка, тип файла) изображение должно прийти на сервер.

Не входит: vision-анализ скриншота агентом (`vision_analyze`) — это отдельный пайплайн для чата.

---

## 2. Два способа отправить скриншот

| Способ | Где | Когда |
|--------|-----|--------|
| **А. Hermes.Wpf (Command Center)** | Кнопка «Скриншот» / команда агенту | Автопубликация после захвата, если включена в окне **WordPress** |
| **Б. Hermes.WpGallery.Tool** | Отдельная утилита | Ручной или периодический захват экрана |

Оба используют одну библиотеку **`Hermes.WpGallery`** и один REST-контракт плагина **Hermes Image Receiver**.

---

## 3. Создание скрinшота

### 3.1. Hermes.Wpf (основной путь)

```
Кнопка «Скриншот» / триггер в чате
        │
        ▼
DesktopScreenCaptureService.CapturePrimaryMonitor()
        │
        ▼
ScreenCapturePipeline (Hermes.DesktopCapture)
        │
        ├── screen_YYYYMMDD_HHmmss.png          ← plain (без разметки)
        ├── screen_YYYYMMDD_HHmmss_regions.png  ← с номерами окон (для vision, не для WP)
        └── screen_YYYYMMDD_HHmmss.json         ← метаданные регионов
```

- **Формат файла на диске:** PNG (`Format32bppArgb` → PNG).
- **Папка по умолчанию:** `%LocalAppData%\HermesWpf\screenshots\`.
- **На WordPress уходит только plain PNG** (`capture.ImagePath`), не `*_regions.png`.

### 3.2. Hermes.WpGallery.Tool

- Захват через GDI+ (`CopyFromScreen`).
- **Формат на выбор в настройках:** PNG или JPEG (качество JPEG 10–100%).
- Имя файла: `screenshot_YYYYMMDD_HHmmss.{png|jpg}`.

---

## 4. Формат отправки на WordPress (главное)

### 4.1. Основной endpoint (использует Hermes.Wpf)

**Метод и URL:**

```http
POST https://ВАШ-САЙТ/wp-json/hermes/v1/message
Content-Type: application/json; charset=utf-8
```

**Тело запроса — один JSON-объект:**

```json
{
  "type": "image",
  "sender": "camera1",
  "image_base64": "<стандартный Base64 без префикса data:...>"
}
```

| Поле | Обязательно | Описание |
|------|-------------|----------|
| `type` | да | Для картинки **строго** `"image"`. Для проверки связи — `"text"`. |
| `sender` | да | Канал галереи (группировка на сайте). Пусто → `"unknown"` или имя ПК. |
| `image_base64` | да | **Сырые байты PNG/JPEG/GIF**, закодированные в Base64 (RFC 4648). **Не** URL, **не** `data:image/png;base64,`. |

**Чего в `/message` нет:**  
поля `mime`, `filename`, `token`, multipart/form-data — клиент WPF их **не отправляет**; плагин определяет тип по **магическим байтам** файла после `base64_decode`.

**Минимальный размер:** после декодирования ≥ 10 байт, иначе `400 Invalid image data`.

**Поддерживаемые типы изображения (автоопределение на сервере):**

| Сигнатура (начало файла) | MIME | Расширение на диске WP |
|--------------------------|------|-------------------------|
| `\x89PNG\r\n\x1a\n` | `image/png` | `.png` |
| `\xFF\xD8\xFF` | `image/jpeg` | `.jpg` |
| `GIF87a` / `GIF89a` | `image/gif` | `.gif` |
| иное | `image/png` (fallback) | `.png` |

**Рекомендация для Hermes.Wpf:** отправлять **PNG** — так захват делается по умолчанию, без потерь.

**Успешный ответ (HTTP 200):**

```json
{
  "success": true,
  "id": 42,
  "url": "https://site.com/wp-content/uploads/hermes-images/2026/06/capture-....png"
}
```

**Ошибки:**

```json
{ "error": "image_base64 required for type image" }
{ "error": "Invalid image data" }
```

### 4.2. Legacy endpoint (токен, явный MIME)

Используется старыми интеграциями; WPF по умолчанию идёт через `/message`.

```http
POST https://ВАШ-САЙТ/wp-json/hermes/v1/image
Content-Type: application/json
X-Hermes-Token: <токен из настроек плагина>   ← или поле "token" в JSON
```

```json
{
  "token": "секрет",
  "channel": "camera1",
  "filename": "screenshot_20260611_120000.png",
  "mime": "image/png",
  "data": "<base64>",
  "sender": "optional",
  "meta": {}
}
```

Поле `data` или `image_base64` — те же сырые байты в Base64.

---

## 5. Цепочка в коде (Hermes.Wpf)

```
RunDesktopScreenCaptureUiAsync (MainViewModel)
        │
        ├── CapturePrimaryMonitor() → PNG на диск
        │
        └── HermesGalleryPublisher.TryPublishPlainScreenshotAsync(capture)
                    │
                    ├── читает capture.ImagePath (plain PNG)
                    ├── WpGalleryImageFrame(bytes, mime, filename, …)
                    └── WpGalleryClient.UploadAsync()
                              │
                              └── POST /wp-json/hermes/v1/message
                                  JSON: type, sender, image_base64
```

**Условия автопубликации:**

- `HermesGallerySiteUrl` задан в окне **WordPress** (Command Center).
- `HermesGalleryPublishEnabled` = true («После скрinшота агента…»).
- Файл plain PNG существует.

**Логи:** строки `[wp-gallery] agent: uploading …` / `POST /message ok` в `%AppData%\HermesWpf\logs\`.

---

## 6. Настройка на стороне WordPress

1. Установить плагин **Hermes Image Receiver** (v1.0.6+).
2. Проверка: `GET /wp-json/hermes/v1/status` → поле `version`.
3. На странице галереи: шорткод `[hermes_gallery channel="camera1"]`, где `camera1` = значение `sender` / `HermesGalleryChannel`.
4. Live-обновления: SSE `GET /wp-json/hermes/v1/stream?channel=…`.

Файлы сохраняются в `wp-content/uploads/hermes-images/YYYY/MM/`.

---

## 7. Пример ручной отправки (curl)

Подставьте свой сайт и путь к PNG:

```bash
B64=$(base64 -w0 screen_20260611_120000.png)

curl -sS -X POST "https://example.com/wp-json/hermes/v1/message" \
  -H "Content-Type: application/json" \
  -d "{\"type\":\"image\",\"sender\":\"camera1\",\"image_base64\":\"$B64\"}"
```

PowerShell (Windows):

```powershell
$bytes = [IO.File]::ReadAllBytes("C:\path\screen.png")
$b64 = [Convert]::ToBase64String($bytes)
$body = @{ type = "image"; sender = "camera1"; image_base64 = $b64 } | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "https://example.com/wp-json/hermes/v1/message" -Method Post -Body $body -ContentType "application/json; charset=utf-8"
```

---

## 8. Краткая шпаргалка «формат для WordPress»

| Вопрос | Ответ |
|--------|--------|
| Transport | HTTP POST, **JSON**, не multipart |
| Endpoint | `/wp-json/hermes/v1/message` |
| Картинка в JSON | поле **`image_base64`** — только Base64 байтов |
| Префикс `data:image/...` | **Нельзя** |
| Лучший формат из Hermes.Wpf | **PNG** (plain, без `_regions`) |
| JPEG | Допустим, если байты начинаются с `\xFF\xD8\xFF` |
| Канал | поле **`sender`** (строка) |
| Токен для `/message` | **Не нужен** |
| Мин. размер | 10 байт после decode |

---

## 9. Связанные файлы

| Файл | Роль |
|------|------|
| `Hermes.Wpf/Services/DesktopScreenCaptureService.cs` | Захват экрана |
| `Hermes.DesktopCapture/ScreenCapturePipeline.cs` | PNG + regions + JSON |
| `Hermes.Wpf/Services/HermesGalleryPublisher.cs` | Публикация после агента |
| `Hermes.WpGallery/WpGalleryClient.cs` | HTTP POST + Base64 |
| `Hermes.WpGallery/WpGalleryEndpoints.cs` | Пути REST |
| `WordPressPlugins/.../class-hir-rest-api.php` | Приём на сайте |
| `Hermes.WpGallery.Tool/` | Утилита с PNG/JPEG и таймером |

---

*Отчёт подготовлен по коду репозитория Hermes, 2026-06-11.*
