# Hermes.Wpf — вложения в чат

**Дата:** 2026-08-16  
**Проект:** `Hermes.Wpf`  
**Версия UI:** в заголовке окна — `Hermes Command Center {Version} {Debug|Release}` (`AppVersion`)

---

## Назначение

Пользователь прикрепляет файлы и изображения к исходящему сообщению. Копии сохраняются локально в проекте; в `hermes chat` уходят **пути** (WSL), без парсинга ответа CLI. Превью в UI одинаковые **до и после** отправки.

Соответствует backlog §5.1 в [`Docs/Plans/TASKS_2026-06-11.md`](../../Plans/TASKS_2026-06-11.md).

---

## Как пользоваться

| Действие | Как |
|----------|-----|
| Файл(ы) | Кнопка **📎 Файл** или drag-and-drop в поле ввода |
| Скрин монитора | **🖼 Скрин** |
| Картинка из буфера | **Ctrl+V** в поле ввода |
| Убрать одно / все | ✕ на чипе / **Очистить** |
| Просмотр | Клик по превью → окно масштаба |

Лимит: до **12** вложений на сообщение.

---

## Хранение

Копии: `{project}/hermes/attachments/` (создаётся при первом импорте).

Форматы превью: `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.bmp`. Остальные файлы — чип с именем без миниатюры.

---

## UI

1. **Pending** (над полем ввода) — `Chat.PendingAttachments`: чипы 120×90 + имя.
2. **После Send** — то же через `ChatMessage.PreviewAttachments` (из `Attachments` / `AttachmentPaths` / `ImagePath`).

Заголовки: `AppVersion.MainWindowTitle` / `ChatWindowTitle` (`0.1.0` из `Hermes.Wpf.csproj` + Debug/Release).

---

## Передача в агент

В payload к CLI добавляется блок путей (WSL):

```text
Attached files (local paths for tools/vision — WSL):
- [image] shot.png → /mnt/d/.../hermes/attachments/...
- [file] notes.txt → /mnt/d/.../hermes/attachments/...
```

В пузыре чата: текст пользователя + строка `📎 …`.

Pure agent pass-through не ломается: WPF не парсит ответ ради вложений.

---

## Код

| Компонент | Путь |
|-----------|------|
| Модель вложения | `Hermes.Wpf/Models/ChatAttachment.cs` |
| Сообщение + `PreviewAttachments` | `Hermes.Wpf/Models/ChatMessage.cs` |
| Импорт в `hermes/attachments` | `Hermes.Wpf/Services/ChatAttachmentStore.cs` |
| Версия в title | `Hermes.Wpf/Services/AppVersion.cs` |
| Pending + команды | `ChatViewModel`, `MainViewModel` |
| UI чипов / drop / paste | `Views/ChatView.xaml(.cs)` |

---

## Вне скоупа (пока)

- Загрузка бинарников вложений в Supabase Storage
- Полноценная персистентность метаданных вложений в history JSON (пути в сессии UI есть; durable history — по мере доработки `HistoryService`)
- Мультичат по проектам (§5.2) и метки времени (§5.3)
