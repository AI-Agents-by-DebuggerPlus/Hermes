# Формат MD-урока для Hermes.EnglishLearning

**Дата:** 2026-07-12  
**Потребитель:** WPF `Hermes.EnglishLearning` (Windows 7+, .NET Framework 4.8)  
**Источник:** агент Hermes генерирует файл → передаёт через Supabase `messages`

---

## 1. Доставка через Supabase

Hermes вставляет строку в `public.messages`:

| Поле | Значение |
|------|----------|
| `sender_name` | `Hermes` |
| `recipient_name` | `EnglishLearning` |
| `content` | JSON (предпочтительно) или сырой MD |

### Предпочтительный `content` (JSON)

```json
{
  "type": "english_lesson",
  "title": "If I Had a Heart",
  "markdown": "---\ntitle: If I Had a Heart\n...\n"
}
```

- `type` обязателен: `english_lesson` (или `english_cards`).
- `markdown` — полный текст урока (см. §2). Экранируйте переносы в JSON (`\n`).
- Не использовать префикс `[LOG:…]`.
- Не класть аудио в Supabase — только текст.

Альтернатива: весь MD как plain `content`, если начинается с `---` / `## title` и содержит `## words` или `## lyrics`.

---

## 2. Структура Markdown

```markdown
---
title: If I Had a Heart
title_ru: Если бы у меня было сердце
artist: Fever Ray
type: english_lesson
version: 1
---

## title
English title | Русский перевод
Artist line | Пояснение

## words
word | перевод
phrase | перевод

## lyrics
English line or short phrase
Русский перевод
---
Next English line
Следующий перевод
```

### Секции

| Секция | Назначение | Экран в приложении |
|--------|------------|--------------------|
| `## title` | Название + перевод (+ артист) | Первый экран(ы) |
| `## words` | Словарь | Столько экранов, сколько влезет слов |
| `## lyrics` | Отрывки песни | Столько экранов, сколько влезет фраз |

Приложение **само пагинирует** по размеру окна — в MD перечисляйте все пары подряд.

### Форматы карточки

1. Одна строка: `English | Russian`
2. Две строки подряд: EN, затем RU
3. В `## lyrics`: блоки, разделённые `---`, внутри EN затем RU (или `EN | RU`)

---

## 3. Инструкция агенту Hermes (фрагмент для промпта)

Когда пользователь просит подготовить урок / карточки по песне или тексту:

1. Сгенерируй MD по формату выше (title → words → lyrics).
2. Опубликуй в Supabase `messages`:
   - `recipient_name=EnglishLearning`
   - `content` = JSON `{"type":"english_lesson","title":"…","markdown":"…"}`
3. В ответе пользователю кратко: сколько слов / фраз и что урок отправлен в English Learning.

Не отправляй TTS JSON (`{"ru":…}{"en":…}`) как урок — это другой протокол.

---

## 4. Пример файла

См. `Hermes.EnglishLearning/SampleLessons/If_I_Had_a_Heart_lesson.md`  
и исходный текст: `Docs/EnglishLearning/If_I_Had_a_Heart_lyrics.md`.
