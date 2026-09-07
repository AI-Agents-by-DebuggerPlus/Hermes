# Самообучение агентов Hermes и harness в Hermes.Wpf

**Дата:** 2026-09-07  
**Источники:** документация Nous Research (Hermes Agent), `~/.hermes/config.yaml` / `memories/`, логи Accountant, skills в репозитории, код `Hermes.Wpf`.  
**Связанный отчёт:** `Docs/Reports/Hermes_CLI_Memory_SelfLearning_Analysis.md` (2026-07-03, кейс water-submit).

---

## 1. Задумка Nous Research

Hermes Agent — агент с замкнутой петлёй обучения. Разработчики разделяют три вида памяти.

| Вид | Где | Когда попадает в контекст |
|-----|-----|---------------------------|
| Факты | `~/.hermes/memories/MEMORY.md` (лимит 2200 символов), профиль `USER.md` (1375) | Снимок в system prompt **в начале сессии** |
| Процедуры | `~/.hermes/skills/**/SKILL.md` | По необходимости (progressive load), не целиком всегда |
| Разговор | `~/.hermes/sessions/` + `--resume` | Только текущая сессия |

Задуманный цикл после нетривиальной задачи:

1. Агент выполняет задачу инструментами.
2. `memory` — короткий обобщённый факт (не проектные секреты и не длинные процедуры).
3. `skill_manage` — сохранить или поправить процедуру (create / patch), если ход повторяемый или после ошибок.
4. Фоновый review может предложить правки skills; есть gate `write_approval`.
5. Следующая сессия читает MEMORY с диска и подхватывает skill по триггеру.

Память **не уплотняется сама**. Если запись не влезает в лимит, инструмент `memory` возвращает ошибку. Агент должен сам сжать или удалить записи и повторить. Снимок в промпте не обновляется до новой сессии.

Skills — предпочтительный способ расширять агента без правки его кода. Формат — `SKILL.md` (agentskills.io): когда применять, шаги, ошибки, проверка.

---

## 2. Что происходит по факту решения задач

Петля **не замыкается сама**. Успех в чате ≠ запись опыта.

### water-submit (2026-07-03)

Агент передал показания через browser, но не обновил `MEMORY.md` и не создал skill. Знание осталось в сессии и в файлах проекта (`instruction.txt`), не в кристаллизованной памяти. Подробности — в отчёте от 2026-07-03.

### Accountant, скрины RBC (2026-09-06…07)

Модель сессии: `google/gemini-3-flash-preview` (OpenRouter).

| Шаг | Факт |
|-----|------|
| Извлечение с `rbc.png` | Суммы транзакций верные |
| Лишнее | Сам посчитал балансы в пустых ячейках, хотя просили только извлечь |
| Позже | Сначала назвал банк TD; после пояснения пользователя — RBC Chequing |
| «Сохрани в память» | Инструмент `memory` не сработал: `MEMORY.md` ~3598 символов при лимите 2200. Агент честно написал, что учёл только в сессии |

Формат RBC и правило «Chequing = основа сумм» записали **снаружи** (Cursor → `AGENTS.md` + skill `chequing-source-of-truth`), не сам CLI-агент через `skill_manage`.

### ProjectManager / KindleConverter (2026-09-06)

Фраза «добавь проект» должна была стать строкой PM Dashboard (`portfolio_add`). Агент начал писать код конвертера. Роль и skill `wpf-pm-dashboard` тоже заданы извне, не выучены агентом после ошибки.

### Общий вывод

Агенты **умеют** читать скрины, ходить в файлы и выполнять задачи. Самообучение Nous (memory + skill после результата) **почти не используется**.

Типичные срывы:

- Процедуру кладут в MEMORY или в болтовню сессии, а не в skill.
- MEMORY переполнена инструкциями (flashcard, scheduler, ProjectManager, Accountant) — инструмент записи фактов молчит.
- Нет auxiliary LLM (`OPENROUTER_API_KEY` / `hermes setup`) — сжатие контекста выкидывает середину диалога без summary.
- Слабая/быстрая модель дополняет пустые поля и не отличает «извлеки» от «посчитай».

Что работает как замена самообучению: файлы workspace (`AGENTS.md`, `hermes/project.md`) и skills, которые ставит человек. Это harness, не learning loop.

---

## 3. Как в Hermes.Wpf сделаны harness’ы

В репозитории «harness» — не один класс, а несколько контуров, которые сужают агента правилами и проверяемыми действиями.

### 3.1 Дисковый harness проекта

`ProjectAgentsBootstrapService.EnsureProjectHermesArtifacts` при выборе или добавлении Agent Workspace создаёт или дополняет:

- `AGENTS.md` — правила проекта (память vs файлы, `Projects/<продукт>/`, голос AndroidChat, краткост);
- `hermes/project.md`, `hermes/credentials.md`, `hermes/screenshots/`.

Hermes CLI читает `AGENTS.md` из cwd. Это основной способ «научить» без записи в MEMORY.

Отдельный жёсткий контур — `HermesProjects/Mt5Developer/hermes/harness/`: `PROTOCOL.md`, шаги, `CURRENT.json`, append-only лог, state задачи. Контролёр сверяет файлы, не текст чата.

### 3.2 Протокол `wpf_local` (CLI → Windows)

Агент в ответе отдаёт JSON `{"skill":"wpf_local","action":"…"}`.  
`WpfLocalIntentParser` вырезает intent, `WpfLocalActionExecutor` делает действие на Windows.

Сейчас это в первую очередь:

- `scheduler_add|list|complete|remove` — напоминания в чат проекта;
- `portfolio_add|list|set_status|remove` — PM Dashboard.

`IsWpfLocalHarnessAction` — только префиксы `scheduler_` и `portfolio_`. Для них post-hook в CLI **не** шлётся (чтобы агент не ушёл в лишний follow-up).

### 3.3 Post-local learning hook

`CliLearningFollowUpService` (если включён `CliPostLocalFollowUpEnabled`): после локального действия WPF снова зовёт CLI и просит кратко подтвердить итог и при повторяемом паттерне предложить память или `skill_save`.

Это мост к задумке Nous, но он не заменяет `skill_manage` и на harness-CRUD (scheduler/portfolio) отключён.

### 3.4 Специальные режимы, не pure agent

По `hermes-core-philosophy`: в режиме агента WPF не подмешивает свой промпт и не парсит ответ. Трейдинг, репетитор, карточки — legacy-исключения. Новые фичи — сначала CLI, WPF только показывает результат и исполняет явный `wpf_local`.

### 3.5 Чем harness не является

- Не Windows Task Scheduler как память агента.
- Не периодический REST poll сообщений (только Realtime).
- Не копия `MEMORY.md` внутри WPF. Sync `WslAgentMemorySyncService` — снимок в vault, не источник правды.

---

## 4. Практическая схема

| Нужно запомнить | Куда | Кто пишет |
|-----------------|------|-----------|
| Как вести себя в этом workspace | `AGENTS.md` | Человек / bootstrap |
| Факты проекта (пути, счета как роли) | `hermes/project.md` | Человек или агент по запросу |
| Короткий переносимый факт | `MEMORY.md` через `memory` | Агент, если есть место |
| Процедура | skill в `~/.hermes/skills/` | Лучше агент (`skill_manage`); сейчас часто человек |
| Учёт инициатив | PM Dashboard через `portfolio_*` | Агент ProjectManager, не код продукта |

Пока MEMORY забита и auxiliary LLM не настроен, надеяться на «сам запомнит» нельзя. Для Accountant и ProjectManager источником правил остаются `AGENTS.md` и установленные skills.
