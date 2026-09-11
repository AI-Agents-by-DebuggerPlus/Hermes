# Самообучение Hermes CLI: компактор памяти, детерминированный триггер skill_manage, эпизодическая память

Цель — заменить надежду «агент сам решит написать в память/skill» на харнесс, который
делает это структурно обязательным, а модель — только исполнителем внутри уже открытого
слота. Три компонента ниже независимы, но собираются в одну петлю в разделе 4.

---

## 1. Компактор `MEMORY.md`

### Проблема
`memory` тул падает молча (или с ошибкой, которую агент не всегда обрабатывает), когда
`MEMORY.md` уже на пределе 2200 символов. Сжатие не automatic — Nous ожидает, что агент
сам сожмёт и повторит попытку, но на практике это не происходит (кейс Accountant/RBC).

### Решение: pre-flight compaction, а не reactive
Не ждать, пока `memory` тул упадёт — проверять бюджет **до** попытки записи и сжимать
превентивно, когда файл переходит порог (например 85% от лимита), а не когда он уже полон.

```
~/.hermes/memories/
  MEMORY.md              # активный файл, читается в system prompt
  MEMORY.archive.jsonl   # append-only, никогда не читается в контекст
  memory_meta.json       # {entry_id: {added_at, last_referenced_at, ref_count, source_session}}
```

### Алгоритм `memory_compactor.py`

1. **Trigger points:** (а) перед каждым вызовом `memory` тула, (б) при старте сессии, если
   файл > 85% лимита.
2. **Скоринг записей** (эвристика, без LLM — дёшево и детерминированно):
   - `age_penalty` = дни с `added_at`
   - `usage_bonus` = `ref_count` (сколько раз запись реально всплывала как релевантная —
     см. §3, эпизодическая память логирует это)
   - `specificity_penalty` — запись, которая выглядит как факт одного проекта
     (regex на пути, имена файлов, конкретные суммы) — кандидат на перенос в
     `hermes/project.md`, а не в глобальный MEMORY
3. **Если после скоринга всё ещё не влезает** — только тогда зовём auxiliary LLM
   (`OPENROUTER_API_KEY`, дешёвая модель) с узкой задачей:
   > "Сожми эти N записей в M записей, не теряя различимых фактов. Не добавляй общих
   > слов. Каждая запись ≤ 1 строка."
   Это единственное место, где LLM участвует — остальное детерминировано.
4. Переписываем `MEMORY.md`, вытесненное — в `MEMORY.archive.jsonl` (не теряется,
   но и не засоряет контекст; доступно эпизодической памяти через retrieval).
5. Только после этого выполняется исходный `memory`-вызов агента.

```python
# hermes/memory_compactor.py (эскиз)
BUDGET = 2200
SOFT_TRIGGER = 0.85

def maybe_compact(memory_path: Path, meta_path: Path) -> None:
    text = memory_path.read_text()
    if len(text) < BUDGET * SOFT_TRIGGER:
        return
    entries = parse_entries(text)               # список строк-фактов
    meta = json.loads(meta_path.read_text())
    scored = sorted(entries, key=lambda e: score(e, meta[e.id]))
    # переносим "project-specific" записи наружу в hermes/project.md, если найден cwd-проект
    project_bound, global_bound = split_by_specificity(scored)
    if project_bound and current_project_root():
        append_to(current_project_root() / "hermes/project.md", project_bound)
    remaining = global_bound
    while total_len(remaining) > BUDGET * 0.8:
        remaining = llm_compress(remaining)      # auxiliary LLM, только если нужно
    archive(memory_path, evicted=entries_not_in(remaining))
    memory_path.write_text(render(remaining))
```

### Ключевой момент
Компакция никогда не блокирует агента отказом — она либо тихо освобождает место
до вызова `memory`, либо (если совсем некуда деть) сама решает, что переносить в
`hermes/project.md`. Агенту не нужно самому «уметь сжимать» — это забота харнесса.

---

## 2. Детерминированный триггер `skill_manage`

### Проблема
Сейчас `skill_manage` вызывается только если модель сама решит, что паттерн повторяемый.
Это ровно то, что не срабатывает (water-submit, ProjectManager/KindleConverter).

### Решение: лог коррекций + обязательный reflection-checkpoint

**2.1. Лог коррекций** — `hermes/corrections.jsonl` в каждом project workspace,
append-only, пишется кодом (не моделью), когда харнесс детектирует сигнал коррекции:

- Явный тул-колл `note_correction(what, expected, actual)` — новый маленький тул,
  который дешевле встроить в промпт-инструкции, чем полагаться на `memory`.
- Эвристический детектор в CLI-обвязке: сообщения пользователя, начинающиеся с
  паттернов "нет,", "не то", "я имел в виду", "это не X, это Y" в первые 2 хода
  после ответа агента — логируются автоматически как `implicit_correction`,
  без участия модели.

```json
{"ts": "2026-09-07T10:14:00Z", "session": "acc-0906", "task_type": "extract_screenshot",
 "what": "bank_name", "actual_first": "TD", "corrected_to": "RBC Chequing", "source": "implicit"}
```

**2.2. Reflection-checkpoint — код, не подсказка**

После завершения сессии (или после N tool-calls подряд без ошибок — "нетривиальная
задача" по терминологии отчёта) харнесс **принудительно** вставляет служебный ход,
а не полагается на то, что агент сам инициирует:

```python
def post_session_hook(session):
    corrections = load_recent_corrections(session.project, session_id=session.id)
    if not corrections and not session.had_nontrivial_tool_use():
        return
    similar = group_by_similarity(corrections, window=last_n_sessions(5))
    candidates = [g for g in similar if len(g) >= 2]  # повторилось ≥2 раза
    if not candidates and not session.had_nontrivial_tool_use():
        return
    # это ОБЯЗАТЕЛЬНЫЙ system-turn, не опциональный follow-up
    force_agent_turn(session, prompt=REFLECTION_PROMPT.format(
        corrections=candidates,
        tools_used=session.tool_summary(),
    ))
```

`REFLECTION_PROMPT` не спрашивает "хочешь сохранить память?" (на это легко ответить
"нет" или проигнорировать) — он структурно требует одного из трёх исходов и парсит
ответ кодом:

```
Реши строго одно из трёх и верни JSON:
{"action": "skill_draft", "name": "...", "content": "..."} |
{"action": "memory_fact", "text": "..."} |
{"action": "none", "reason": "..."}
Если было ≥2 однотипных коррекции за сессию — "none" не принимается без reason,
объясняющего, почему это не повторяемый паттерн.
```

Это тот же паттерн, что `CliLearningFollowUpService` в Hermes.Wpf для `wpf_local`,
но — в отличие от него — **не выключен для основного CLI-агента**, и не опциональный
флаг, а часть обязательного post-session прохода.

**2.3. `write_approval` gate остаётся**

`action: skill_draft` не пишет файл сразу — уходит в `hermes/skills_pending/`,
и либо: (а) авто-approve, если такой же draft-паттерн подтверждён ≥2 раза подряд
без правок человека, либо (б) требует явного «ок» от Serhii при следующем
взаимодействии с этим проектом. Это сохраняет текущий gate, но перестаёт быть
единственным местом, где решение вообще может быть принято.

---

## 3. Эпизодическая память (мост между сессией и MEMORY/skills)

Сейчас есть три уровня (по отчёту): факты (MEMORY.md), процедуры (skills), разговор
(сессии, не персистентные между запусками). Не хватает четвёртого — **эпизодического
лога**, который не грузится в каждый system prompt, но доступен по retrieval и
питает и компактор (§1, `usage_bonus`), и reflection-checkpoint (§2).

```
~/.hermes/episodic/
  2026-09-06_accountant.jsonl   # per-session: task, tools, outcome, corrections, duration
  2026-09-07_pm-dashboard.jsonl
  index.tfidf                    # уже есть паттерн — тот же TF-IDF/Ollama retrieval,
                                  # что в External Brain vault из архитектуры Hermes
```

Периодический (не на каждой сессии — раз в N сессий или раз в неделю) batch-проход
auxiliary LLM по эпизодическому логу:

- ищет паттерны, которые reflection-checkpoint пропустил внутри одной сессии, но
  которые видны только на горизонте нескольких сессий (например, ProjectManager
  трижды получал "добавь проект" и трижды уходил в код вместо `portfolio_add`);
- предлагает skill-draft той же дорожкой через `hermes/skills_pending/`;
- не пишет ничего напрямую в MEMORY.md — эпизодическая память принципиально не тот
  слой, что 2200-символьный факт-снимок.

---

## 4. Как это собирается в одну петлю

```
Сессия агента
   │
   ├─ implicit/explicit коррекции → hermes/corrections.jsonl (код, не модель)
   │
   ▼
post_session_hook (детерминированный, обязательный)
   │
   ├─ есть ≥2 похожих коррекции ИЛИ нетривиальный tool-use?
   │        │да
   │        ▼
   │   force_agent_turn(REFLECTION_PROMPT) → {skill_draft | memory_fact | none+reason}
   │        │
   │        ├─ memory_fact → memory_compactor.maybe_compact() → запись в MEMORY.md
   │        │                  (если не влезает — авто-сжатие/перенос в project.md)
   │        │
   │        └─ skill_draft → hermes/skills_pending/ → авто-approve после 2 повторов
   │                          без правок, иначе — подтверждение человеком
   │
   └─ session log → ~/.hermes/episodic/*.jsonl (всегда, независимо от исхода)
                        │
                        ▼
              batch review (раз в N сессий, auxiliary LLM)
                        │
                        └─ кросс-сессионные паттерны → тот же skills_pending pipeline
```

Ключевая идея всех трёх частей одна: **решение "запомнить/не запомнить" больше не
принимается моделью в моменте** — оно либо принудительно вызывается кодом
(reflection-checkpoint), либо происходит вообще без модели (детекция коррекций,
скоринг при компакции). LLM остаётся только там, где нужна семантика — сжатие текста
и формулировка skill-контента, — а не там, где нужна дисциплина процесса.

---

## 5. Точки интеграции с текущим кодом

| Компонент | Куда встраивать |
|---|---|
| `memory_compactor.py` | Обёртка вокруг существующего `memory` тула в hermes CLI, вызывается pre-flight |
| `note_correction` тул + implicit-детектор | Новый тул в реестре hermes CLI + regex-хук в CLI message loop |
| `post_session_hook` | Аналог `CliLearningFollowUpService`, но на стороне CLI, не WPF, и не отключаемый для основного агента (в отличие от `IsWpfLocalHarnessAction`-исключения) |
| `hermes/skills_pending/` + auto-approve счётчик | Рядом с существующими `hermes/project.md`, `hermes/credentials.md` в per-project workspace |
| `~/.hermes/episodic/` + `index.tfidf` | Тот же ретривал-паттерн, что уже используется в External Brain vault — переиспользовать код, а не писать заново |
| Конфиг | Новые ключи в `~/.hermes/config.yaml`: `memory.soft_trigger_ratio`, `reflection.min_corrections_for_forced_turn`, `skills_pending.auto_approve_after` |

## 6. Порядок внедрения (чтобы не сломать то, что уже работает)

1. Сначала §1 (компактор) — самый низкий риск, чисто механический, сразу убирает
   молчаливые отказы `memory`.
   **Статус (2026-09-10):** сделано. `scripts/hermes/memory_compactor.py` + pre-flight
   в `HermesService` перед `hermes chat`. Первый прогон: MEMORY ~3319→~1670 символов;
   вытесненное в `MEMORY.archive.jsonl`.
   **Статус (2026-09-11):** LLM-сжатие подключено — OpenRouter (ключ из Settings /
   `OPENROUTER_API_KEY`), только если после project-split всё ещё >80% бюджета;
   иначе детерминированный pack-by-score.
2. Затем `note_correction` + implicit-детектор (без forced turn) — просто начинаем
   собирать `corrections.jsonl`, ничего не меняя в поведении агента.
   **Статус (2026-09-10):** сделано в Hermes.Wpf.
   - Implicit: 2 user-хода после ответа агента, regex («нет,», «не то», «я имел в виду», …)
     → append `HermesProjects/<ws>/hermes/corrections.jsonl`.
   - Explicit: JSON `{"skill":"note_correction",...}` из ответа CLI → тот же файл.
   - AGENTS patch + skill draft `scripts/hermes/skills/note-correction/`.
   Forced turn / skill_draft — ещё нет (п.3–4).
3. После недели данных — включаем `post_session_hook` с forced turn, сначала только
   с `action: none, reason: ...` обязательным (проверить, что парсинг работает,
   не создавая пока ни одного реального skill).
   **Статус (2026-09-10):** сделано в Hermes.Wpf.
   - Триггер: **New CLI session** / сброс сессии (кнопка или фраза), если в
     `corrections.jsonl` ≥2 записи этой сессии (или ≥1 при ≥3 ответах агента).
   - Forced `hermes chat` с reflection prompt; парсер `ReflectionOutcomeParser`.
   - `none` + reason принимается; `skill_draft` / `memory_fact` только в
     `hermes/skills_pending/` + `reflection_log.jsonl` (**не** применяются).
4. Только потом — включаем `skill_draft` → `skills_pending` → auto-approve.
   **Статус (2026-09-10):** сделано в Hermes.Wpf (`SkillsPendingService`).
   - `skill_draft` → `hermes/skills_pending/`; одинаковый fingerprint **×2** →
     auto-install в `~/.hermes/skills/domain/<slug>/SKILL.md`.
   - Ручной approve: фраза `approve pending skill` / `ок skill`.
   - `memory_fact` → запись в `MEMORY.md` после compact (если влезает).
5. Эпизодическая память (§3) — последняя, она зависит от накопленного `corrections.jsonl`
   и сессионных логов за несколько недель, иначе batch review нечего анализировать.
   **Статус (2026-09-11):** сделано в Hermes.Wpf (`EpisodicMemoryService`).
   - События → `~/.hermes/episodic/YYYY-MM-DD_<project>.jsonl` (agent_reply,
     implicit/note_correction, reflection, session_closed).
   - При закрытии CLI-сессии: счётчик; каждые **5** сессий — детерминированный
     batch: паттерн ≥**3** → `skill_draft` через `SkillsPendingService`.
   - Простой `index.tfidf.json`; fold `corrections.jsonl` из HermesProjects.
   **Статус (2026-09-11):** auxiliary-LLM batch подключён — OpenRouter (тот же ключ
   Settings → ИИ-помощник) поверх детерминированных паттернов; drafts →
   `skills_pending` / auto-approve. Без ключа — только детерминизм.
