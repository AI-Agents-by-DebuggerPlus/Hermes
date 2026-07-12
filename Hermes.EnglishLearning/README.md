# Hermes.EnglishLearning

WPF-клиент для разучивания английского по карточкам (песни / тексты).

- **ОС:** Windows 7+ (целевой фреймворк **.NET Framework 4.8**)
- **UI:** тёмный фон, EN жёлтым, RU светло-серым (как на референсе)
- **Источник уроков:** локальный MD или Supabase `messages` (`type=english_lesson`)
- **TTS:** локальный Windows SAPI (без аудио через Supabase)

## Запуск

```powershell
dotnet build Hermes.EnglishLearning\Hermes.EnglishLearning.csproj -c Debug
dotnet run --project Hermes.EnglishLearning\Hermes.EnglishLearning.csproj
```

Нужен установленный **.NET Framework 4.8 Developer Pack** / targeting pack.

## Управление

| Клавиша / кнопка | Действие |
|------------------|----------|
| ← → / PageUp/Down / Space | Экраны |
| S / «Озвучить» | TTS текущего экрана |
| Esc | Стоп TTS |
| Открыть MD… | Локальный урок |
| Supabase… | URL, anon key, recipient |

## Документация

- `Docs/EnglishLearning/CARD_MD_FORMAT.md` — формат MD + контракт для Hermes
- `Docs/EnglishLearning/TTS_OPTIONS.md` — варианты озвучки
- Sample: `SampleLessons/If_I_Had_a_Heart_lesson.md`
