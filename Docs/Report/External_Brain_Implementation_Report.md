# Отчёт: реализация External Brain (Hermes.Wpf)

**Дата:** 2026-05-01  
**Область:** модуль «внешний мозг» — чтение локальных Markdown-файлов (vault в стиле Obsidian), поиск, контекст для промпта Hermes, отдельное окно UI.

---

## 1. Назначение и границы

Hermes реализует **только файловый слой**: рекурсивное чтение `*.md` из настроенной папки, кэш в памяти, отслеживание изменений на диске, построение текстового блока для CLI Hermes.

**Не реализовано (и не заявлено как часть модуля):**

- Прямое подключение к **Omi** (нет API, синхронизации, учётных данных).
- Прямое подключение к **Obsidian** как приложению (нет Local REST API, плагинов, WebSocket).
- Запись/редактирование заметок из Hermes (только чтение с диска).

Связка «Omi → Obsidian → AI» с точки зрения Hermes сводится к тому, что пользователь указывает **ту же папку**, куда Omi/Obsidian уже пишут `.md` файлы.

---

## 2. Основные компоненты

| Компонент | Путь | Роль |
|-----------|------|------|
| Сервис | `Hermes.Wpf/Services/ExternalBrainService.cs` | Загрузка, кэш, watcher, поиск, `BuildContext` |
| Парсинг MD | `Hermes.Wpf/Services/ExternalBrainMarkdown.cs` | Теги `#tag`, очистка тела, дата из имени файла при наличии |
| Превью MD | `Hermes.Wpf/Services/MarkdownFlowPresenter.cs` | Упрощённый рендер в `FlowDocument` (WPF) |
| Модель | `Hermes.Wpf/Models/MemoryItem.cs` | `Timestamp`, `Content`, `Tags`, `SourceFile`, доп. поля для UI |
| Конфиг overlay | `Hermes.Wpf/Models/ExternalBrainFileConfig.cs` | JSON: `MemoryPath` |
| Настройки | `Hermes.Wpf/Models/HermesSettings.cs` | Путь, флаг инъекции, лимит фрагментов |
| VM окна | `Hermes.Wpf/ViewModels/ExternalBrainViewModel.cs` | Список, фильтры, debounce поиска, команды |
| Окно | `Hermes.Wpf/Views/ExternalBrainWindow.xaml` (+ `.xaml.cs`) | Поиск, ListView, группировка по дате, превью |

Интеграция в приложение: `MainWindow.xaml.cs` создаёт сервис и передаёт в `MainViewModel`; кнопка открывает `ExternalBrainWindow`; при закрытии настроек вызывается `RestartWatcherAndReload`; при выходе — `Dispose` сервиса.

---

## 3. Разрешение пути к «памяти»

Эффективный путь вычисляется в `ExternalBrainService.ResolveEffectiveMemoryPath()` в порядке **приоритета**:

1. Переменная окружения **`HERMES_EXTERNAL_BRAIN_PATH`** — если непустая и каталог существует.
2. Файл **`%AppData%\HermesWpf\externalBrain.json`** — поле `MemoryPath`, если каталог существует (ошибки чтения логируются как предупреждение).
3. **`HermesSettings.ExternalBrainMemoryPath`** из `settings.json` (редактируется в UI настроек, есть обзор папки).

Если путь пустой или каталог не существует, кэш очищается, watcher не ставится; UI показывает инструкцию с пометкой **USER ACTION REQUIRED**.

---

## 4. Загрузка и кэш

- Все файлы `*.md` перечисляются **рекурсивно** (`SearchOption.AllDirectories`).
- Результат хранится в **`ImmutableList<MemoryItem>`** под блокировкой; операции чтения делают снимок списка.
- Повторная полная перезагрузка с диска: `ReloadFromDiskAsync` выполняет тяжёлую работу в **`Task.Run`**, затем уведомляет подписчиков на UI-потоке.
- Флаг **`_busyLoad`** не допускает параллельных полных перезагрузок.

---

## 5. Парсинг одного файла (`TryParseMarkdownFile`)

- Текст читается целиком; **`RawMarkdown`** сохраняется в модели.
- **Время (`Timestamp`):** по умолчанию **`LastWriteTimeUtc`** файла; если `ExternalBrainMarkdown.TryGetFilenameTimestamp` извлекает дату из имени файла — она используется (как локальное время, далее приводится к отображению в локали).
- **Теги:** извлекаются хэштеги из текста (`#tag`), нормализация в списке тегов — по логике `ExternalBrainMarkdown`.
- **Содержимое для контекста/UI:** `CleanContentBody` — убирает YAML frontmatter при наличии, ведущие строки-заголовки и лишние пустые строки по правилам модуля.

---

## 6. Поиск и контекст для AI

**Публичные методы сервиса (из кэша, без повторного чтения диска для каждого запроса):**

- `GetAllMemoriesAsync` — все элементы, сортировка по `Timestamp` убыв.
- `SearchAsync(query)` — токенизация запроса, скоринг совпадений с полями заметки, сортировка по скору и дате.
- `GetRecentAsync(TimeSpan)` — отсечение по UTC.
- `GetByTagAsync(tag)` — точное совпадение нормализованного тега (после trim и снятия ведущего `#`).
- `BuildContextAsync(userQuery, maxItems)` и синхронная обёртка **`BuildContext`** — формирование одного текстового блока с заголовками/разделителями; при нулевом скоре подставляются самые свежие записи до лимита; лимит ограничивается (например, 1–50).

**Интеграция в чат:** в `MainViewModel` перед отправкой в Hermes, если **`ExternalBrainInjectIntoPrompt`**, вызывается `BuildContextAsync` с текстом пользовательского сообщения и **`ExternalBrainMaxContextItems`**. Блок передаётся в сборку исходящего промпта (`BuildOutboundHermesPrompt`); длина логируется. Ошибки не рвут отправку — пишется предупреждение в лог.

---

## 7. FileSystemWatcher

- Создаётся на **эффективном корневом пути** vault, **`IncludeSubdirectories = true`**, фильтр **`*.md`**.
- События `Changed`, `Created`, `Deleted`, `Renamed` планируют **отложенную перезагрузку** (~450 ms debounce) на **Dispatcher** приложения, чтобы сгладить серии событий от редактора.
- После перезагрузки вызывается **`MemoriesChanged`** — окно External Brain обновляет список через подписку во ViewModel.

При смене настроек пути вызывается **`RestartWatcherAndReload`** (пересоздание watcher + перезагрузка).

---

## 8. UI (External Brain)

- Поиск: **`TextBox`** с привязкой к `SearchText`, **debounce** на `DispatcherTimer` (~380 ms).
- Кнопка **«Найти»** — `SearchCommand` (немедленное применение фильтров без ожидания debounce).
- Фильтр по тегу (строка), пресеты времени: **все / сегодня / 7 дней** (`ExternalBrainViewModel.TimeChoices`).
- Список: **`ListView`** + **`ICollectionView`**: сортировка по `Timestamp` DESC, **группировка** по `MemoryItem.DateGroupKey` (дата `yyyy-MM-dd`).
- Панель просмотра: выбранный `MemoryItem` → **`FlowDocument`** через `MarkdownFlowPresenter`.
- **«Обновить»** — принудительный reload с диска; **«Папка vault»** — открытие проводника, если путь валиден.
- При закрытии окна: отписка от **`MemoriesChanged`** (`DetachFromBrainEvents`).

---

## 9. Настройки в `HermesSettings`

| Поле | Назначение |
|------|------------|
| `ExternalBrainMemoryPath` | Путь к корню vault (Windows) |
| `ExternalBrainInjectIntoPrompt` | Включить ли блок памяти в исходящий промпт (по умолчанию `true`) |
| `ExternalBrainMaxContextItems` | Максимум фрагментов в блоке (клампится при загрузке/сохранении настроек) |

Нормализация в **`SettingsService`**.

---

## 10. Производительность и потоки

- Кэш в RAM; поиск и `BuildContext` работают по **снимку** кэша.
- Полный перечень файлов с диска — только при reload (старт, watcher, явное обновление UI).
- UI-таймеры и `FileSystemWatcher` завязаны на **WPF Dispatcher**; уведомление UI после фоновой загрузки — через диспетчер.

---

## 11. Опциональные возможности из исходного ТЗ (не доведены)

- Отдельный UI для **редактирования тегов** в файлах — нет (есть только фильтр по тегу).
- **Pin / избранное** для записей — нет.
- **Скоринг релевантности** для контекста и поиска — **есть** (токены запроса против содержимого/метаданных).

---

## 12. Зависимости и сборка

Модуль не добавляет отдельных NuGet для Obsidian/Omi; используются стандартные API .NET и WPF. Сборка проекта `Hermes.Wpf` — как обычно; при блокировке `Hermes.Wpf.exe` другим процессом шаг копирования apphost может завершиться ошибкой MSB3027 (закрыть приложение перед сборкой).

---

## 13. Краткий вывод

Реализована **полная цепочка «папка Markdown → кэш → поиск/фильтры → UI → опциональный блок в промпте Hermes»** с приоритетом пути через env / JSON / settings и с **FileSystemWatcher**. Интеграции **Omi** и **Obsidian** как продуктов отсутствуют; совместимость достигается указанием **одного и того же каталога vault** на диске, который эти инструменты уже обновляют.
