# Задачи: Hermes CLI — навыки (skills) для решения задач

**Статус:** backlog (будущая реализация)  
**Создано:** 2026-07-01  
**Контекст:** кейс Reni Water — локальный обработчик Hermes.Wpf перехватывает фразы до CLI и ставит задачу в Windows Task Scheduler. Для текущей передачи показаний это приемлемо; в перспективе оркестрацию должен вести **Hermes CLI**.

---

## Цель

Hermes CLI (агент) должен **сам** решать бытовые и повторяющиеся задачи:

1. **Использовать имеющиеся навыки** — встроенные (`builtin_reni_water`, `wpf_local`, …) и сохранённые в `~/.hermes/skills/` / `%AppData%\HermesWpf\skills\`.
2. **Создавать новые навыки**, если подходящего нет (кристаллизация: `skill_save`, `HERMES_SKILL_CRYSTALLIZE_*`, manifest + run-скрипт).
3. **Помнить использование инструментов** — в т.ч. `schtasks`, terminal, `wpf_local` JSON — через сессию CLI и память (`MEMORY.md`, vault).

---

## Текущее состояние (на 2026-07-01)

| Аспект | Сейчас | Желаемое |
|--------|--------|----------|
| «Передай показания» / «завтра в водоканал» | `ReniWaterLocalChatHandler` в WPF, до вызова CLI | CLI выбирает навык → `wpf_local` или terminal |
| Pure CLI mode | Без WPF prompt blocks; локальный перехват — исключение | CLI получает skill resolver + каталог в промпте |
| Расписание | WPF → `schtasks` напрямую | CLI → `schtasks` или `wpf_local reni_water_schtasks_register` |
| Генерация навыков | `SkillGenerationService`, auto-crystallize после N успехов Reni | CLI инициирует сохранение по задаче пользователя |

**Связанные документы:**
- `Docs/Reports/AssistantModeReport/03_Skill_Generation.md`
- `Docs/Reports/AssistantModeReport/04_Reni_Water_Case_Study.md`
- `Docs/Reports/AssistantModeReport/05_Gaps_And_Recommendations.md`
- `Hermes.Wpf/Services/SkillResolverInstructions.cs`
- `Hermes.Wpf/Services/WpfLocalInstructions.cs`

---

## Задачи (backlog)

### 1. Skill resolver в потоке Hermes CLI

- [ ] В **Pure CLI mode** (или всегда) подмешивать в outbound промпт: `SkillResolverInstructions`, `WpfLocalInstructions`, компактный каталог `GeneratedSkillCatalog`.
- [ ] Ранжировать навыки по тексту задачи (`GeneratedSkillTaskMatcher`) и передавать top-N в промпт.
- [ ] После ответа CLI парсить `run_generated`, `wpf_local`, `skill_save` (уже частично есть в non-pure path).

### 2. CLI-first для бытовых автоматизаций (Reni Water и аналоги)

- [ ] Документировать контракт: CLI возвращает `{"skill":"wpf_local","action":"reni_water_submit"}` и т.д.
- [ ] Для расписания: CLI вызывает `schtasks /Create` **сам** (terminal tool) или `wpf_local reni_water_schtasks_register` / `reni_water_schedule`.
- [ ] Сохранять в память CLI факт: URL, env-путь, расписание, last run — не полагаться только на WPF settings.
- [ ] **Не удалять** `ReniWaterLocalChatHandler` до стабильного CLI-path; затем — режим «fallback» или feature flag.

### 3. Создание навыков по запросу

- [ ] Если matcher score < порога и задача повторяемая — CLI предлагает/выполняет кристаллизацию (`skill_save`).
- [ ] Шаблоны для kind=`script` (PowerShell/Python) и kind=`intent` (wpf_local).
- [ ] Синхронизация vault ↔ `~/.hermes/skills/` (`GeneratedSkillVaultSyncService`).

### 4. Единый реестр навыков для агента

- [ ] Объединить в промпт: `AgentSkillsCatalog`, generated skills, built-in (`builtin_reni_water`, flashcards, tutor, vision).
- [ ] Inject статуса Reni (`settings`, `pending_ack`, schtasks) в system block при упоминании водоканала (см. Gaps doc §2).

### 5. Приёмочные критерии

- [ ] Фраза «Отправь завтра показания в водоканал» в **Pure CLI** без локального перехвата: CLI создаёт schtasks или wpf_local schedule, ответ с датой/временем.
- [ ] «Передай показания» → CLI → `wpf_local reni_water_submit` → Playwright → скриншот в чат.
- [ ] «Ты передавал показания?» → ответ из памяти/vault/settings, без «нет доступа к ЖКХ».
- [ ] Новая задача без навыка → CLI сохраняет skill и повторно использует на следующий раз.

---

## Примечание по текущей задаче (2026-07-02)

Разовая передача показаний **02.07.2026 09:00** выполняется по **текущей реализации**:

`Windows Task Scheduler` → `Hermes_ReniWater_OnceSubmit` → `scripts/reni_water/run_submit.ps1`

Изменения в планировщике для этой даты **не требуются**.

---

## Done: open-url-avg (2026-08-22)

BioStack / магазины с Cloudflare (iHerb): Playwright `browser_navigate` не использовать.

- Skill: `~/.hermes/skills/domain/open-url-avg/` (зеркало в репо: `Docs/BioStack/skills/open-url-avg/`)
- Открывает **AVG Secure Browser**; в чате URL кликабельны (`ChatMessageLinkifier`)
- После смены правил — **New CLI Session**, иначе старая сессия продолжает звать Playwright
- Док: [`Docs/BioStack/README.md`](../BioStack/README.md)

*Обновлять по мере реализации. Связанный инцидент: чат Utilities 2026-07-01, задача `Hermes_ReniWater_OnceSubmit`.*
