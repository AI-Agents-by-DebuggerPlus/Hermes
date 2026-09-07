# Отчёт: формирование контента и отображение в Hermes.EnglishLearning.Xp

**Дата:** 2026-07-26  
**Источник:** `Hermes.EnglishLearning.Xp` (WinForms, .NET 4.0)  
**Цель:** реализовать аналогичную логику отображения урока в Android (Kotlin), ориентир — **горизонтальный (landscape)** экран.

Связанные документы:

- `Docs/EnglishLearning/CARD_MD_FORMAT.md` — формат MD-урока / Supabase
- `Docs/EnglishLearning/ENGLISH_NAV_ANDROIDCHAT.md` — навигация и TTS page publish
- `Docs/SupaBase/Формат_TTS_Android_Assistant.md` — bilingual `{"en":…,"ru":…}`

Код-эталон:

| Модуль | Файл |
|--------|------|
| Модели | `Hermes.EnglishLearning.Xp/Models.cs` |
| Парсер MD + pager | `Hermes.EnglishLearning.Xp/LessonLogic.cs` |
| Отрисовка | `Hermes.EnglishLearning.Xp/MainForm.cs` (`RenderCurrent`, `AddCard`) |
| TTS страницы | `Hermes.EnglishLearning.Xp/PageTtsFormatter.cs` |
| Настройки пагинации | `Hermes.EnglishLearning.Xp/SettingsStore.cs` |

---

## 1. Конвейер данных (pipeline)

```
Supabase messages.content
        │
        ▼
 MessageParser.TryExtractLesson  →  markdown (+ optional title)
        │
        ▼
 LessonMarkdownParser.Parse      →  LessonDocument
        │                            (TitleCards, Words, Phrases, Lyrics)
        ▼
 LessonPager.Build               →  List<LessonScreen>
        │                            (фиксированные «страницы» / экраны)
        ▼
 UI index 0..N-1                 →  RenderCurrent(screen)
        │
        ▼ (при смене экрана)
 PageTtsFormatter.FormatScreen   →  bilingual JSON lines → AndroidChat
```

На Android достаточно повторить те же три слоя: **Parse → Page → Render**.  
Пагинация в XP **не** зависит от высоты окна: число карточек на экран задаётся константами настроек. Это упрощает порт на Kotlin.

---

## 2. Модель данных

### 2.1. `CardPair`

Минимальная единица UI и TTS:

| Поле | Смысл |
|------|--------|
| `en` | Английский текст (верхняя строка карточки, акцент) |
| `ru` | Русский перевод (нижняя строка) |

Любое поле может быть пустым (например, строка артиста только в `en`).

### 2.2. `LessonDocument`

После парсинга MD:

| Коллекция | Секция MD | Порядок в уроке |
|-----------|-----------|-----------------|
| `titleCards` | `## title` | 1 |
| `words` | `## words` | 2 |
| `phrases` | `## phrases` | 3 |
| `lyrics` | `## lyrics` | 4 |

Мета из YAML front matter: `title` / `title_en`, `title_ru`, `artist`.

### 2.3. `LessonScreen` (страница UI)

| Поле | Смысл |
|------|--------|
| `section` | `Title` / `Words` / `Phrases` / `Lyrics` |
| `sectionLabel` | Подпись сверху: `"Title"`, `"Words"`, `"Phrases"`, `"Sentences"` |
| `cards` | Подмножество `CardPair` на этот экран |
| `columnCount` | Число колонок сетки (1…3); у Words обычно 2 |

Навигация Next/Previous двигает **индекс в плоском списке** `screens`, а не внутри секции.

Kotlin-скелет:

```kotlin
data class CardPair(val en: String, val ru: String)
enum class LessonSection { Title, Words, Phrases, Lyrics }
data class LessonScreen(
    val section: LessonSection,
    val sectionLabel: String,
    val cards: List<CardPair>,
    val columnCount: Int = 1
)
```

---

## 3. Входной контент (как появляется урок)

### 3.1. Supabase

- `recipient_name = EnglishLearning`
- `content` — JSON:

```json
{"type":"english_lesson","title":"…","markdown":"---\n…"}
```

Либо сырой markdown (начинается с `---` / `## title` и содержит `## words` или `## lyrics`).

Типы `english_lesson` и `english_cards` эквивалентны.  
Поле markdown: `markdown` | `md` | `content`.

### 3.2. Локальный файл

Тот же MD с диска (`SampleLessons`, папка lessons).

---

## 4. Парсинг Markdown → карточки

### 4.1. Front matter (опционально)

Между первой парой `---`:

```
title: …
title_ru: …
artist: …
```

### 4.2. Секции `## …`

Нормализация имени секции (prefix match):

| Заголовок | Канон |
|-----------|--------|
| title… | `title` |
| word… / vocab… | `words` |
| phrase… / example… | `phrases` |
| lyric… / sentence… / line… | `lyrics` |

### 4.3. Форматы строк карточки

**A. Pipe (предпочтительно для words/title):**

```
English | Русский
```

**B. Две строки подряд:** EN, затем RU.

**C. Для `phrases` и `lyrics` — блоки через `---`:**  
внутри блока те же A/B; каждый блок → одна или несколько карточек через `ParsePipeLines`.

Пустые строки, `#…`, одиночный `---` (как разделитель блоков) не создают карточку сами по себе.

Если `titleCards` пуст, но есть meta `title` — добавляется карточка `(TitleEn, TitleRu)` и при наличии `artist` — `(Artist, "")`.

---

## 5. Пагинация (`LessonPager.Build`)

Порядок экранов строго: **Title → Words → Phrases → Lyrics**.  
Пустая секция пропускается.

Нарезка: `chunk(cards, perPage)` с параметрами:

| Секция | `perPage` (default) | `columnCount` |
|--------|---------------------|---------------|
| Title | `CardsPerScreenOther` = **3** | **1** |
| Words | `CardsPerScreenWords` = **8** | `WordColumns` = **2** (clamp 1…3) |
| Phrases | `CardsPerScreenOther` = **3** | **1** |
| Lyrics | `CardsPerScreenOther` = **3** | **1** |

Минимумы в коде: words ≥ 2 на экран, other ≥ 1.

**Важно для Android:** пагинация **не** пересчитывается по `MeasureText` / высоте.  
Один и тот же MD → один и тот же список экранов при тех же настройках.  
Для landscape можно **увеличить** `cardsPerScreenWords` / `cardsPerScreenOther` и/или `wordColumns` (например 3), не меняя алгоритм.

Псевдокод:

```kotlin
fun buildScreens(doc: LessonDocument, cfg: PagerConfig): List<LessonScreen> {
    val out = mutableListOf<LessonScreen>()
    fun add(section: LessonSection, label: String, cards: List<CardPair>, perPage: Int, cols: Int) {
        if (cards.isEmpty()) return
        cards.chunked(perPage).forEach { page ->
            out += LessonScreen(section, label, page, cols)
        }
    }
    add(Title, "Title", titleCardsOrFallback(doc), cfg.otherPerPage, 1)
    add(Words, "Words", doc.words, cfg.wordsPerPage, cfg.wordColumns)
    add(Phrases, "Phrases", doc.phrases, cfg.otherPerPage, 1)
    add(Lyrics, "Sentences", doc.lyrics, cfg.otherPerPage, 1)
    return out
}
```

---

## 6. Визуальное отображение (как в XP)

### 6.1. Макет экрана

```
┌─────────────────────────────────────────────┐
│ [chrome: title / buttons]     (optional)    │  ← на Android в landscape можно скрыть
├─────────────────────────────────────────────┤
│ SectionLabel  (muted, ~11sp × scale)        │
│                                             │
│   ┌──────────┐  ┌──────────┐               │  ← grid: columnCount колонок
│   │ EN bold  │  │ EN bold  │               │
│   │ RU muted │  │ RU muted │               │
│   └──────────┘  └──────────┘               │
│   …                                         │
├─────────────────────────────────────────────┤
│ status: команды / TTS     │  3/16  ×1.00   │
└─────────────────────────────────────────────┘
```

### 6.2. Сетка карточек

Для текущего `LessonScreen`:

```
cols = max(1, screen.columnCount)
usableWidth = contentWidth - horizontalPadding
colWidth = usableWidth / cols

for (i, card) in cards.withIndex():
    col = i % cols
    row = i / cols
    x = leftPad + col * colWidth
    y = topAfterLabel + row * rowHeight
    drawCard(card, x, y, colWidth - gap)
```

Порядок заполнения: **слева направо, сверху вниз** (row-major).

### 6.3. Карточка

Двухстрочный блок, текст **по центру** ячейки:

| Строка | Роль | XP (scale=1) | Цвет XP |
|--------|------|--------------|---------|
| EN | Основная | ~18pt Bold | `#F8D12F` (акцент) |
| RU | Перевод | ~13pt Regular | `#D0D4DC` |

Высоты строк карточки (до scale): EN ≈ 36px, RU ≈ 28px.

### 6.4. Высота ряда (`rowHeight`)

Зависит от секции (до scale):

| Section | rowHeight |
|---------|-----------|
| Lyrics | 110 |
| Phrases | 90 |
| Words / Title | 72 |

Все геометрические константы умножаются на `UiScale` (0.6…2.5, default 1.0).

### 6.5. Фон / тема

| Элемент | Цвет |
|---------|------|
| Фон контента | `#0B0E11` |
| Chrome / status | `#121A28` |
| Muted label | `#AAB2C0` |
| Текст UI | `#EAECEF` |

Шрифт XP: Tahoma. На Android: `sans-serif` / Roboto, для EN можно semi-bold.

### 6.6. Индикатор прогресса

`"{index+1} / {screens.size}"` (+ опционально `×scale`).

---

## 7. Рекомендации для Android landscape

Горизонтальный экран шире и ниже → цель: **больше колонок / больше карточек на экран**, без смены парсера.

### 7.1. Предлагаемые defaults (landscape phone / tablet)

| Параметр | XP default | Landscape phone | Landscape tablet |
|----------|------------|-----------------|------------------|
| `wordColumns` | 2 | **3** | **3–4** |
| `cardsPerScreenWords` | 8 | **9–12** | **12–16** |
| `cardsPerScreenOther` | 3 | **2–3** (длинные фразы) | **3–4** |
| EN text size | 18sp | 16–20sp | 20–24sp |
| RU text size | 13sp | 12–14sp | 14–16sp |

Для `Phrases` / `Lyrics` оставлять **1 колонку** (длинные строки), как в XP.

### 7.2. Layout Compose / View

- `Activity` / destination: `screenOrientation=landscape` или `sensorLandscape`.
- Контент: `LazyVerticalGrid` с `columns = Fixed(screen.columnCount)` **или** ручная сетка как в XP (предсказуемее для совпадения с page TTS).
- Полноэкранный режим обучения: скрыть system bars + top chrome; оставить тонкий progress.
- Масштаб: pinch или кнопки ±; хранить в `SharedPreferences` как `uiScale`.

### 7.3. Совпадение с TTS со страницы XP

Если Android **показывает** тот же экран, что XP отправил на озвучку, pager-параметры на телефоне должны совпадать с отправителем **или** Android должен озвучивать **свой** текущий `LessonScreen` тем же `PageTtsFormatter` (рекомендуется).

Формат TTS страницы (одна строка JSON на карточку, порядок `en` затем `ru`):

```text
{"en":"heart","ru":"сердце"}
{"en":"voice","ru":"голос"}
```

Парсер TTS на AndroidChat уже умеет ordered `en`/`ru` (см. формат Assistant).

### 7.4. Навигация

Те же команды, что принимает XP (`english_nav` / `[NAV:…]`):

| command | UI |
|---------|-----|
| `next` | index++ |
| `previous` / `prev` | index-- |
| `fullscreen` | immersive on/off |
| `exit` | finish Activity |

Локально: swipe left/right или кнопки; клавиши медиа при желании.

---

## 8. Чеклист портирования на Kotlin

1. [ ] `LessonMarkdownParser` — front matter + секции + pipe/двухстрочные/блоки `---`  
2. [ ] `LessonPager.build` — chunk по секциям, те же defaults + landscape overrides  
3. [ ] UI: `SectionLabel` + grid карточек EN/RU, тёмная тема  
4. [ ] State: `screens: List<LessonScreen>`, `index`  
5. [ ] Supabase: принять `english_lesson`, отфильтровать `recipient_name`  
6. [ ] (Опционально) publish page TTS при смене экрана  
7. [ ] Landscape defaults: 3 колонки для Words, больше карточек на экран  
8. [ ] Прогресс `i / N`  
9. [ ] Тест на sample: `SampleLessons/Official_Hermes_Docs_lesson.md`, `If_I_Had_a_Heart_lesson.md`

---

## 9. Краткая формула «как выглядит экран»

1. Распарсить MD → четыре списка пар EN|RU.  
2. Нарезать списки на страницы фиксированного размера.  
3. На странице: подпись секции + сетка `columnCount × rows`, каждая ячейка = **EN сверху (жёлтый) + RU снизу (серый)**, по центру.  
4. Next/Prev = соседняя страница в общем списке.  
5. В landscape увеличить колонки/ёмкость страницы Words, фразы и предложения оставить одноколоночными.

Этого достаточно, чтобы Android Kotlin визуально и по структуре экранов совпал с `Hermes.EnglishLearning.Xp`.
