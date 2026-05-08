# Репетитор английского: ошибки `/bin/bash` в логах Hermes.Wpf

Документ для **перепроверки позже**: что наблюдалось, почему это происходило, какие изменения внесены в клиент и что ещё стоит проверить.

## Симптомы в `%AppData%\Roaming\HermesWpf\logs\hermes_session_*.log`

Сообщения в потоке как `[TERM] [stderr]`, например:

- `/bin/bash: line 1: $'english-tutor-session\r': command not found` и далее разбор JSON как «команд» (`phase::`, `words::` и т.д.)
- `/bin/bash: command substitution: line 1: syntax error near unexpected token '|'`
- `/bin/bash: command substitution: line 1: '|'`

Ответ модели при этом может выглядеть нормально; stderr идёт от инструментов Hermes CLI / вложенного shell, а не от UI WPF.

## Корневые причины (кратко)

1. **CRLF в тексте** — bash трактует `\r` как часть токена (`$'...\r'`).
2. **Старый формат отчёта** — fenced-блок вида `` ```english-tutor-session`` … строки попадают в shell построчно.
3. **Символ `|` в промпте/ответе** — в bash оператор конвейера; при прохождении фрагмента через shell даёт ошибки вида «command not found» по частям строки.
4. **Обратная кавычка ASCII `` ` ``** — в bash подстановка команд (command substitution); сочетание вроде `` `|` `` приводит к ошибкам именно около `|`.
5. **Контент External Brain / заметок** — если в контекст попадут таблицы Markdown с колонками через `|`, теоретически возможны похожие эффекты при передаче в nested shell Hermes.

## Изменения в клиенте Hermes.Wpf (на момент написания)

| Область | Файл / поведение |
|--------|-------------------|
| Нормализация перевода строк перед `hermes chat` | `HermesService.SendMessageAsync` — `ReplaceLineEndings("\n")` |
| Защита от backtick в payload | `HermesService` — замена ASCII `` ` `` на U+FF40 (fullwidth grave) в сообщении чата и в quick actions |
| Формат машинного отчёта репетитора | `EnglishTutorPromptDefaults` — маркеры `HERMES_TUTOR_SESSION_BEGIN` / `END`, без тройных fences; пример JSON **без** `|` в тексте инструкций |
| Парсинг на стороне WPF | `EnglishTutorVocabularyStore` — маркеры + legacy fence для совместимости |

При необходимости сверьтесь с актуальными версиями этих файлов в репозитории.

## Чеклист для повторной проверки

1. Открыть **самый новый** `hermes_session_*.log` после короткой сессии репетитора.
2. Поиск по файлу: `stderr`, `english-tutor-session`, `command substitution`, `command not found`.
3. Убедиться, что запущен **exe из свежей сборки** Release (или Debug), без «залипшего» старого процесса во время сборки.
4. Если ошибки сохраняются **только** при включённом External Brain — проверить заметки с таблицами (`|`-строки); при подтверждении добавить санитизацию pipe в `ExternalBrainService.BuildContextAsync` или в общем слое перед вызовом Hermes.

## Связанные материалы

- Поведение памяти / «Save experience»: `Docs/Test_ExternalBrain_Memory_and_Learning.ru.md`
