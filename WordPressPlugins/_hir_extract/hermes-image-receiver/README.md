# Hermes Image Receiver — WordPress Plugin

Плагин получает изображения по **WebSocket** от **Hermes.Wpf** и отображает их на сайте в реальном времени.

---

## Быстрый старт

### 1. Установка

```
wp-content/plugins/hermes-image-receiver/
```

Активируйте плагин в **Плагины → Установленные**.

---

### 2. Настройка

**Настройки → Hermes Receiver**

| Параметр | По умолчанию | Описание |
|---|---|---|
| Хост | 0.0.0.0 | IP-адрес WebSocket-сервера |
| Порт | 8765 | Порт WebSocket |
| Макс. изображений | 50 | Лимит в галерее |
| Сохранять на диск | ✓ | Сохранять файлы в uploads |
| Секретный токен | (авто) | Для авторизации от Hermes.Wpf |

---

### 3. Hermes.Wpf — настройка

В приложении Hermes.Wpf укажите:

```
WebSocket URL:  ws://ваш-сайт.ru:8765
Token:          <значение из настроек плагина>
```

**Формат JSON-сообщения:**

```json
{
  "type":     "image",
  "token":    "ваш-токен",
  "channel":  "camera1",
  "filename": "frame_001.png",
  "mime":     "image/png",
  "data":     "<base64>",
  "meta": {
    "width": 1920, "height": 1080,
    "timestamp": "2024-01-15T10:30:00Z"
  }
}
```

---

### 4. Шорткод на страницах

Вставьте шорткод в любую страницу или запись:

```
[hermes_gallery]
```

**Параметры:**

```
[hermes_gallery 
  channel="camera1"
  max="20"
  autoconnect="true"
  ws_port="8765"
  layout="grid"
  title="Камера 1"
]
```

| Параметр | Варианты | По умолчанию |
|---|---|---|
| `channel` | любая строка | (все каналы) |
| `max` | 1–1000 | 20 |
| `autoconnect` | true/false | true |
| `layout` | grid / masonry / single | grid |
| `ws_port` | 1024–65535 | 8765 |

---

### 5. Альтернатива — REST API

Если WebSocket недоступен, можно отправлять изображения напрямую:

```
POST /wp-json/hermes/v1/image
Content-Type: application/json

{
  "token":    "ваш-токен",
  "channel":  "default",
  "filename": "photo.jpg",
  "mime":     "image/jpeg",
  "data":     "<base64>"
}
```

Или через HTTP-заголовок:
```
X-Hermes-Token: ваш-токен
```

---

### 6. Требования

- WordPress 5.8+
- PHP 7.4+
- MySQL 5.7+ / MariaDB 10.3+
- Открытый порт на сервере (по умолчанию 8765) — для WebSocket

> **Примечание:** Встроенный WebSocket-сервер PHP требует отдельного процесса.  
> Рекомендуется использовать **REST API endpoint** (пункт 5) вместе с nginx/Apache proxy,  
> либо запускать Node.js/Python WebSocket-сервер-прокси, который пересылает в REST.

---

### 7. Структура файлов

```
hermes-image-receiver/
├── hermes-image-receiver.php   # Главный файл плагина
├── includes/
│   ├── class-hir-activator.php # Активация, создание таблицы БД
│   ├── class-hir-settings.php  # Страница настроек в admin
│   ├── class-hir-rest-api.php  # REST API endpoints
│   └── class-hir-shortcode.php # Шорткод [hermes_gallery]
└── assets/
    ├── css/
    │   ├── gallery.css          # Стили галереи (frontend)
    │   └── admin.css            # Стили страницы настроек
    └── js/
        ├── gallery.js           # WebSocket-клиент + галерея
        └── admin.js             # Admin UI логика
```
