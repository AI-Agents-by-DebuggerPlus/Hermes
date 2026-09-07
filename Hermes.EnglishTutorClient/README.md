# Hermes.EnglishTutorClient

Удалённый клиент репетитора (.NET 4.8 / WPF). Связь с агентом — только через Supabase.

## Режимы

| Индикатор | Поведение |
|-----------|-----------|
| Supabase подключён | Контент и проверка — удалённый агент (проект **English Tutor**) |
| Нет связи | Локальный demo-тест из `SampleExercises/` |

## Горячие клавиши

| Клавиша | Действие |
|---------|----------|
| Enter | Отправить |
| Shift+Enter | Новая строка |
| F11 | Полный экран |
| Escape | Стоп TTS / выход из FS |
| Ctrl+S | Озвучить |
| Ctrl+T | Старт теста |
| Ctrl+L | Лог |
| Ctrl+, | Настройки |
| Ctrl+I | Фокус ввода |
| Play на гарнитуре | Старт/стоп голосового ввода |

## Wire-протокол (Hermes → клиент)

См. `HermesProjects/English Tutor/AGENTS.md` — JSON `tutor_exercise` / `tutor_feedback`.

Имена: `sender_name=EnglishTutorClient`, ответы Hermes на `recipient_name=EnglishTutorClient`.
