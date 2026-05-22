# Persistent Memory и Skill Generation в Hermes (актуально для Hermes.Wpf)

Документ описывает **концепцию** и **реальную реализацию** в экосистеме Hermes: WPF-клиент + WSL Hermes CLI + External Brain vault.

**Hermes «знает» эту логику в рантайме:** Hermes.Wpf добавляет блок `HermesPlatformKnowledgeInstructions` в каждый исходящий `hermes chat` и синхронизирует полный отчёт в vault `Knowledge/Hermes/Experience_and_Skills_Logic.md` (из `Docs/Report/Experience_And_Skills_Logic_Report.md`). Подробности: `Docs/Report/Experience_And_Skills_Logic_Report.md`.

## 1. Три уровня памяти

| Уровень | Концепция | Реализация в Hermes.Wpf |
|--------|-----------|-------------------------|
| **Short-term** | Лог текущей сессии | `ChatViewModel`, история проекта на диске, Supabase relay |
| **Long-term (семантика)** | Векторный поиск по опыту | External Brain vault (`*.md`) + **TF-IDF / Ollama embeddings** (`MemoryVectorIndex`), лог `Querying vector memory…` |
| **Логическая** | SOUL.md, правила агента | Частично: `USER.md` / `MEMORY.md` из WSL → vault; prompt blocks в Settings |

### Настройки памяти (Settings → External Brain)

- **Папка vault** — Obsidian-style Markdown
- **В промпт Hermes** — `ExternalBrainInjectIntoPrompt`
- **Vector memory** — TF-IDF + опционально Ollama (`nomic-embed-text`)
- **WSL memory sync** — `~/.hermes/memories/` → vault

### Проверка памяти

1. Запишите факт в vault или скажите Hermes что-то важное (при включённом relay/memory).
2. Перезапустите Hermes.Wpf или смените проект.
3. Спросите про сохранённый факт — в логе: `[vector-memory] Querying vector memory…`

---

## 2. Skill Generation (Voyager-like)

### Алгоритм в Hermes.Wpf

1. **Решение задачи** — обычный `hermes chat` (WSL).
2. **Рефлексия** — фраза «сохрани как навык» → блок **REFLECTIVE PHASE** с excerpt чата.
3. **Sandbox** — `run.ps1` / `run.py` во temp (`[skill-sandbox] Executing task in sandbox…`).
4. **Кристаллизация** — JSON `{"skill":"skill_save",…}` → папка навыка.
5. **Индекс** — `index.json` в `%AppData%\HermesWpf\skills` и зеркало `~/.hermes/skills/index.json`.
6. **External Brain** — `vault/Procedures/GeneratedSkills/Skill_<id>.md`.

### Структура навыка

```
%AppData%\HermesWpf\skills\<id>\
  manifest.json
  SKILL.md
  run.ps1 | run.py
```

Зеркало (опционально): `~/.hermes/skills/<id>/`

### Типы (`kind`)

| kind | Поведение |
|------|-----------|
| `script` | Локальный запуск по триггерам или «запусти навык `<id>`» |
| `prompt` | Блок `outbound_prompt_block` в каждый исходящий промпт |
| `intent` | JSON `{"skill":"run_generated","id":"…"}` |

### Настройки (Settings → Skill generation)

| Параметр | По умолчанию |
|----------|--------------|
| Skill generation | вкл |
| Mirror to WSL | вкл |
| Sandbox before save | вкл |
| Smoke-test (test_command) | вкл |
| Sandbox timeout | 60 s |
| **Автовыбор навыка под задачу** | вкл |
| Min match score | 0.28 |

### Автовыбор навыка (Skill resolver)

Перед `hermes chat` клиент ранжирует сохранённые навыки по задаче (TF-IDF + ключевые слова: zip, архив, запак…).

Пример: *«Запакуй папку folder в zip-архив»* при наличии навыка `manage_zip` → лог `[skill-resolver] task → manage_zip` и блок **Skill resolver** в промпте: Hermes должен выбрать `{"skill":"run_generated","id":"manage_zip"}` вместо переписывания с нуля.

### UI

Вкладка **«Навыки»** → блок **«Сгенерированные навыки»**: список, запуск, вкл/выкл, открыть папку.

### Проверка генерации навыков

1. Решите задачу в чате (например, простой PowerShell-скрипт).
2. Напишите: **«Сохрани как навык»**.
3. Hermes должен вернуть только JSON `skill_save`.
4. В логе:
   - `[skill-gen] user requested skill crystallization`
   - `[skill-sandbox] Executing task in sandbox…`
   - `[skill-gen] Skill '…' successfully generated and saved`
   - `[skill-index] wrote …`

### Пример JSON (ответ Hermes)

```json
{
  "skill": "skill_save",
  "id": "manage_zip_downloads",
  "title": "Проверка ZIP в Downloads",
  "summary": "Сканирует Downloads, логирует битые архивы.",
  "triggers": ["zip downloads", "битые архивы"],
  "kind": "script",
  "script_extension": "ps1",
  "script_body": "Write-Output 'ok'",
  "test_command": "powershell -NoProfile -ExecutionPolicy Bypass -File run.ps1"
}
```

---

## 3. WSL Hermes CLI (будущее / частично)

Конфиг `~/.hermes/config.toml` из ранних описаний **не управляет Hermes.Wpf** напрямую. WPF использует `%AppData%\HermesWpf\settings.json`.

Для CLI-агента в WSL:

- Память: `~/.hermes/memories/` (синхронизируется в vault)
- Навыки: `~/.hermes/skills/` + `index.json` (зеркало из WPF)

---

## 4. Что ещё не реализовано

- Docker sandbox (сейчас: temp + timeout + фильтр опасных команд)
- Автоматическая кристаллизация без фразы пользователя
- Регистрация навыков как native tools в Hermes CLI
- SOUL.md self-edit
