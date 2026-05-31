# Combined Reports

Regenerated 2026-05-25. Concatenated content of files inside Reports/.

---

## Source: `README.md`

# Reports Package

Папка `Reports/` собирает аналитические отчёты по подсистемам Hermes для команды и
для последующего экспорта в External Brain.

## Аналитика накопления опыта и навыков

- `Experience_Learning_Skills.md`  
  Полная карта подсистем накопления опыта, самообучения и создания навыков:
  Hermes.Wpf (External Brain, MemoryExtractor, RoleExperienceCapture,
  GeneratedSkill*), мост в WSL Hermes CLI (`~/.hermes/memories`,
  `~/.hermes/skills`) и интеграции в трейдинг-приложении.

- `Trading_Platform_Learning_Touchpoints.md`  
  Точки контакта Hermes.TradingPlatform с обучающим контуром: журнал сделок
  (`TradeJournalFileWriter`), in-app ассистент (OpenRouter через
  `Hermes.InAppAssistant`), risk profile с авто SL/TP, market data feed и
  как платформа поставляет факты в `RoleExperienceCapture` для роли Trader.

## Историческая диагностика подключений (как было)

- `Connection_Log_Report_2026-04-29.md`  
  Отчёт по сессионным логам Hermes WPF (анализ `WSL_E_DISTRO_NOT_FOUND`).

- `Project_Structure.md`  
  Снимок структуры Hermes.Wpf на момент разбора логов.

- `Code_Context.md`  
  Ключевые фрагменты `ConnectionService` и `HermesService` для контекста
  диагностики подключения.

## Сводный документ

- `Combined_Report.md`  
  Конкатенация всех файлов выше (для отправки одним документом).

## Связанные источники

- `Docs/Report/Experience_And_Skills_Logic_Report.md` — расширенный отчёт по тому
  же контуру; именно он автоматически выгружается в External Brain через
  `HermesPlatformKnowledgeSyncService`.
- `Docs/Logs/hermes_session_*.log` — журналы WPF, на которых строилась
  историческая диагностика.
- `Docs/Report/Hermes_Trading_Platform_Integration.md` — мост Hermes.Wpf ↔
  Hermes.TradingPlatform.

---

## Source: `Experience_Learning_Skills.md`

# Накопление опыта, самообучение и создание навыков в Hermes

**Дата:** 2026-05-25  
**Область:** Hermes.Wpf + WSL Hermes CLI + Hermes.TradingPlatform (точки касания).  
**Связанные документы:**
[`Docs/Report/Experience_And_Skills_Logic_Report.md`](../Docs/Report/Experience_And_Skills_Logic_Report.md),
[`Docs/Report/External_Brain_Implementation_Report.md`](../Docs/Report/External_Brain_Implementation_Report.md),
[`Trading_Platform_Learning_Touchpoints.md`](Trading_Platform_Learning_Touchpoints.md).

---

## 1. Два контура: память и навыки

В кодовой базе **«опыт»** и **«навыки»** реализованы разными подсистемами,
но они подмешиваются в один и тот же исходящий промпт Hermes.

| Контур | Цель | Где живёт | Автоматизм |
|--------|------|-----------|------------|
| **Память (External Brain)** | Длинная память: семантика, процедуры, эпизоды, identity | Markdown vault Obsidian-стиля (`*.md`) + опциональный векторный индекс | Частичный: ручное сохранение черновика; **роль-aware автозахват**; sync с WSL CLI |
| **Навыки (generated skills)** | Переиспользуемые инструменты под задачи (script / prompt / intent) | `%AppData%\HermesWpf\skills\<id>\` + зеркало `~/.hermes/skills/<id>/` | Подбор под задачу — авто; **создание нового** — только по явному `skill_save` / фразе «сохрани как навык» |

```mermaid
flowchart LR
  subgraph wpf [Hermes.Wpf]
    Chat[Chat input]
    Brain[ExternalBrainService]
    Vec[MemoryVectorIndex]
    Extractor[MemoryExtractorService]
    Capture[RoleExperienceCapture]
    Roles[RoleManager + RoleAwareMemoryRouter]
    Catalog[GeneratedSkillCatalogService]
    Matcher[GeneratedSkillTaskMatcher]
    Gen[SkillGenerationService + SkillSandboxService]
    Runner[GeneratedSkillRunner]
    Vault[(Vault *.md)]
  end

  subgraph wsl [WSL: hermes CLI]
    Mem[~/.hermes/memories\nUSER.md / MEMORY.md]
    Sk[~/.hermes/skills/<id>]
    Cli[hermes chat]
  end

  subgraph trade [Hermes.TradingPlatform]
    Journal[(trade_journal.jsonl)]
    InApp[AppAssistantService]
  end

  Chat --> Brain
  Brain --> Vec
  Brain --> Vault
  Mem -. WslAgentMemorySyncService .-> Vault
  Chat --> Cli
  Cli --> Mem
  Cli --> Chat
  Chat --> Extractor --> Capture
  Capture -. Knowledge/<role>/ .-> Vault
  Chat --> Roles --> Brain
  Chat --> Matcher --> Catalog
  Chat --> Gen --> Catalog
  Gen --> Sk
  Catalog --> Runner
  Catalog -. GeneratedSkillVaultSyncService .-> Vault
  trade -. context only .-> InApp
  Journal -. RoleExperienceCapture (role=Trader) .-> Vault
```

---

## 2. Память: External Brain

### 2.1. Сервис и кэш

`Hermes.Wpf/Services/ExternalBrainService.cs`

- При старте `MainViewModel` создаёт `ExternalBrainService(LogService, HermesSettings, Dispatcher)`.
- Корень vault разрешается в `ResolveEffectiveMemoryPath()` по приоритету:
  1. переменная окружения `HERMES_EXTERNAL_BRAIN_PATH`,
  2. `%AppData%\HermesWpf\externalBrain.json` (поле `MemoryPath`),
  3. `HermesSettings.ExternalBrainMemoryPath`.
- При старте и любом изменении `*.md` (`FileSystemWatcher`, debounce ≈ 450 мс)
  выполняется `ReloadFromDiskAsync` → перепарсивает все Markdown файлы в
  `ImmutableList<MemoryItem>` и пересобирает `MemoryVectorIndex`.
- Парсинг YAML frontmatter (`type`, `timestamp`, `tags`, `project`, `importance`)
  выполняет `ExternalBrainMarkdown`. Метка времени берётся из YAML, иначе из
  имени файла, иначе из `File.LastWriteTimeUtc`.

### 2.2. Подмешивание в промпт

- В `MainViewModel.ExecuteHermesUserTurnAsync` зовётся
  `BuildContextAsync(userQuery, ExternalBrainMaxContextItems)`.
- Если включён `ExternalBrainVectorRetrievalEnabled` — кандидаты идут из
  `MemoryVectorIndex.SelectTopAsync` (cosine similarity, при наличии Ollama —
  плотные эмбеддинги; fallback — TF-IDF).
- Иначе используется лексический скор `MemoryLexicalScorer`.
- Кандидаты дополнительно фильтруются и буститься `RoleAwareMemoryRouter`
  под текущую роль (см. §4).
- Получаемый блок встраивается как `--- EXTERNAL BRAIN ... ---` в исходящий
  промпт через `BuildOutboundHermesPrompt`, **не** показывается в чате.

### 2.3. UI

- Вкладка «External Brain» (`ExternalBrainWindow`, `ExternalBrainViewModel`) —
  поиск, теги, превью.
- Вкладка «WSL Memory» (`WslMemoryView`, `WslMemoryViewModel`) — просмотр
  файлов из `~/.hermes/memories/`.

---

## 3. Извлечение опыта из чата

### 3.1. Черновик (`MemoryDraft`)

`Hermes.Wpf/Services/MemoryExtractorService.cs`

После каждого успешного ответа Hermes (`MainViewModel.ExecuteHermesUserTurnAsync`):

```csharp
_lastExperienceDraft = _memoryExtractor.ExtractExperience(payload, displayResponse);
```

- Эвристики `Classify` → тип `semantic | procedural | episodic`:
  - `semantic` для вопросов «что такое / how does / explain»;
  - `episodic` если в ответе сигналы ошибки (`error`, `exception`, `timeout`);
  - `procedural` при списках / длинном ответе;
  - иначе по длине ответа.
- `ScoreImportance` — 1…5 по объёму и типу (`procedural` получает +1).
- `InferTags` — добавляет `hermes`, `chat`, тип; ловит `dotnet`/`supabase`/etc.

Сохранение черновика на диск **не** автоматическое (без RoleAutoCapture):
пользователь жмёт «Save experience» в `MemoryEditorWindow`, и сервис
кладёт `*.md` в подпапку `Knowledge/`, `Procedures/`, `Projects/` или
`Identity/` (см. `MemoryExtractorService.MemorySubfolderForType`).

### 3.2. Авто-захват по роли (`RoleExperienceCapture`)

`Hermes.Wpf/Services/RoleExperienceCapture.cs`

Это и есть «самообучение» в работающем приложении.
Условия захвата (см. `TryCaptureCore`):

1. `HermesSettings.RoleAutoCapture == true`;
2. активная роль ≠ `AgentRole.Universal`;
3. длина пары вопрос+ответ ≥ `RoleAutoCaptureMinLength` (по умолчанию 150);
4. `Importance ≥ RoleAutoCaptureMinImportance` (по умолчанию 4);
5. `draft.Type` ∈ {`procedural`, `semantic`}; `episodic` не захватывается;
6. `MemoryExtractorService.ShouldSave(draft) == true`;
7. дедупликация по SHA-256 от первых 200 символов задачи+ответа,
   окно последних 50 хэшей.

Если все условия выполнены — сервис кладёт `*.md` в `Knowledge/<Role>`:

| Роль | Папка vault | Тег |
|------|-------------|-----|
| Trader | `Knowledge/Trading` | `trading` |
| Developer | `Knowledge/Development` | `development` |
| English Tutor | `Knowledge/English` | `english` |
| Personal Manager | `Knowledge/Productivity` | `productivity` |
| Universal | (не сохраняется) | — |

После записи `ExternalBrainService.RestartWatcherAndReload("role-capture")` —
кэш и векторный индекс обновляются немедленно.

### 3.3. Where it is wired

`MainViewModel.ExecuteHermesUserTurnAsync` (упрощённо):

```csharp
_lastExperienceDraft = _memoryExtractor.ExtractExperience(payload, displayResponse);
_roleManager.RecordTurn(payload);
var vaultPath = _externalBrain.ResolveEffectiveMemoryPath();
if (_lastExperienceDraft is not null
    && await _roleExperienceCapture
        .TryCaptureAsync(_lastExperienceDraft, _roleManager.CurrentRole, vaultPath)
        .ConfigureAwait(true))
{
    _externalBrain.RestartWatcherAndReload("role-capture");
}
_chatLogService.AppendMessage(project.Name, "Hermes", displayResponse);
SyncWslAgentMemoryToVault("after-chat");
SyncPlatformKnowledgeToVault("after-chat");
_ = WslMemory.RefreshAsync();
```

---

## 4. Маршрутизация по ролям

`Hermes.Wpf/Services/RoleManager.cs`, `RoleAwareMemoryRouter.cs`,
`RoleSkillIndex.cs`.

- Роли: `Universal`, `Trader`, `Developer`, `EnglishTutor`, `PersonalManager`.
- Переключение: фразами в чате (`RoleManager.TrySwitchRoleFromMessage`,
  алиасы «trader/трейдер/трейдинг», «dev/разработчик», «english/репетитор»…),
  либо из UI настроек. При смене сохраняется `Settings.PersistedAgentRole`.
- `RoleAwareMemoryRouter` под активную роль:
  - бустит записи с «своими» тегами/папками (`trading`, `dotnet`, …),
  - штрафует «чужие» (`Trader` понижает `english`/`grammar` и т.д.),
  - нормализует score и берёт топ-N для `BuildContextAsync`.
- `RoleSkillIndex` хранит счётчики использования навыков по ролям в
  `%AppData%\HermesWpf\role-skill-index.json` и сортирует выдачу
  `GetSkillsForRole(role)` по частоте.

---

## 5. Синхронизация памяти WSL → vault

`Hermes.Wpf/Services/WslAgentMemorySyncService.cs`,
пути — `WslAgentMemoryPaths.cs`.

| Источник WSL | Папка vault | Тип |
|--------------|-------------|-----|
| `~/.hermes/memories/USER.md` | `Identity/WslAgent_USER.md` | identity |
| `~/.hermes/memories/MEMORY.md` | `Knowledge/WslAgent_MEMORY.md` | semantic |

- Триггеры (`MainViewModel.SyncWslAgentMemoryToVault`): старт приложения,
  после каждого успешного хода чата, закрытие окна Settings.
- Условия: `HermesSettings.SyncWslAgentMemoryToExternalBrain == true`,
  vault существует, файл найден через UNC `\\wsl.localhost\<distro>\home\<user>\`.
- Контент разбивается по разделителю `§` (`WslAgentMemoryPaths.SplitEntries`)
  и пишется в Markdown с YAML frontmatter. Запись идёт только если содержимое
  изменилось (`WriteIfChanged`).

---

## 6. Платформенная инструкция в vault

`Hermes.Wpf/Services/HermesPlatformKnowledgeSyncService.cs` берёт
`Docs/Report/Experience_And_Skills_Logic_Report.md` (или встроенный fallback)
и пишет копию в `Knowledge/Hermes/<имя>.md` — Hermes получает справку
о самом себе тем же ретривером, что и пользовательские заметки.

Срабатывает на старте, после чата и после закрытия Settings — параллельно
с `SyncWslAgentMemoryToVault`.

---

## 7. Навыки (generated skills)

### 7.1. Файловая раскладка

```
%AppData%\HermesWpf\skills\<id>\
  manifest.json          # GeneratedSkillManifest (id, title, kind, triggers, …)
  SKILL.md               # agentskills.io-style описание
  run.ps1 | run.py       # если kind=script
~\.hermes\skills\<id>\   # WSL-зеркало (если SkillMirrorToWslHermes=true)
%AppData%\HermesWpf\skills\index.json    # GeneratedSkillIndexService
```

`Manifest` (см. `Hermes.Wpf/Models/GeneratedSkillManifest.cs`):
`Id`, `Title`, `Summary`, `Triggers`, `Kind`(`prompt|script|intent`),
`ScriptFile`, `OutboundPromptBlock`, `TestCommand`, `Roles[]`,
`SourceTurn`, `Enabled`, `CreatedAtUtc`, `DirectoryPath`.

### 7.2. Каталог и подбор под задачу

`GeneratedSkillCatalogService` (`Skills`, `Reload`, `MatchTrigger`, `AllCards`,
`OutboundPromptBlocks`, `CompactCatalogForPrompt`, `TrySetEnabled`,
`FindById`) — единственный источник списка навыков для остальных сервисов.

`GeneratedSkillTaskMatcher.Rank(userTask, role, maxItems, minScore)`
строит TF-IDF индекс по `id + title + summary + triggers + outboundPromptBlock`,
учитывает совпадения триггеров, токенов id и концепт-affinity
(`zip/архив/запак`, `screen/скрин`). Итоговый score =
`0.55 × TF-IDF + 0.45 × lexical`, порог `SkillResolveMinScore` (0.28 по умолчанию).
Лог: `[skill-resolver] task → <id> (score=…, …)`.

Результат подаётся в исходящий промпт как блок «Skill resolver — matched
saved skills for THIS user task»; при score ≥ 0.5 Hermes
рекомендуется вернуть `{"skill":"run_generated","id":"…"}` вместо генерации
заново.

### 7.3. Локальный запуск без CLI

`MainViewModel.TryHandleGeneratedSkillLocalAsync` (см. строки ~2162–2202):

1. Явная фраза «запусти навык `<id>`» (`SkillRunTriggers`) → ищем по id и
   запускаем `GeneratedSkillRunner.RunAsync`.
2. Иначе подбор по триггеру (`GeneratedSkillCatalogService.MatchTrigger`):
   если `kind=script`, навык запускается сразу, иначе пропускается на
   обычный flow (промпт с outbound block).

`GeneratedSkillRunner`:

- `prompt`-навыки сюда не приходят — у них только `OutboundPromptBlock`.
- `script`-навыки запускаются по расширению: `.py → python "<path>"`,
  `.ps1 → powershell.exe -NoProfile -ExecutionPolicy Bypass -File`.
- stdout/stderr собираются, обрезаются до 2000 символов, идут в логи
  `[skill-run]`. Ненулевой exit-code считается провалом.
- `RunTestCommandAsync` исполняет `test_command` из манифеста (smoke).

### 7.4. Создание нового навыка (кристаллизация)

Автоматического создания «после любой удачной задачи» **нет**.
Алгоритм такой:

1. Триггер начала кристаллизации:
   - **Фраза в чате** — `SkillCrystallizeTriggers` («сохрани как навык»,
     «кристаллизуй», `save as skill`, …);
   - **Промпт** — `SkillReflectionService.CrystallizeNowBlockRu` + последние
     ~12 сообщений;
   - **Ответ Hermes** — компактный JSON `{"skill":"skill_save",…}` или блок
     `HERMES_SKILL_CRYSTALLIZE_BEGIN … END`.

2. Разбор: `SkillCrystallizeIntentParser.TryConsumeSaveIntent` →
   `SkillSavePayload`.

3. `SkillGenerationService.TrySaveAsync`:
   1. `SkillSandboxService` — запуск под лимитом времени (`SkillSandboxTimeoutSeconds`),
      regex-фильтры опасных команд; при провале сохранение прерывается.
   2. Создание директории `%AppData%\HermesWpf\skills\<id>` (или `<id>_2…` до
      `SkillMaxGenerationAttempts`).
   3. Запись `run.ps1`/`run.py` (если `script`).
   4. Запись `manifest.json` (включая `sourceTurn`) и `SKILL.md`.
   5. Зеркалирование в `~/.hermes/skills/<id>` (`SkillMirrorToWslHermes`).
   6. Если включено — `RunTestCommandAsync` для smoke-проверки.
   7. `GeneratedSkillVaultSyncService.TryExportSkill` — конспект в
      `Procedures/GeneratedSkills/Skill_<id>.md` с тегами
      `generated-skill, hermes-wpf, skill-gen`.
   8. `GeneratedSkillCatalogService.Reload()` — UI и resolver видят новый навык.

4. Ответ в чат через `SkillCrystallizeIntentParser.UserFacingSaveLine`
   («[skill] Навык «…» сохранён и прошёл smoke-тест.» и т.д.).

### 7.5. Run от Hermes

`SkillCrystallizeIntentParser.TryConsumeRunIntent` ловит
`{"skill":"run_generated","id":"…"}` в ответе и запускает `GeneratedSkillRunner.RunAsync`.
Идентификаторы проверяются по `^[a-z][a-z0-9_]{2,47}$`.

---

## 8. Сквозной сценарий: один ход чата

`MainViewModel.ExecuteHermesUserTurnAsync` (упрощённо):

1. Локальные обработчики раньше CLI: ручные торговые ордера/закрытие позиций
   (если включён режим трейдинга), flashcards, Reni Water, скриншот,
   **generated skill trigger/run**, desktop window focus, описание экрана.
2. `ExternalBrainService.BuildContextAsync` — кандидаты опыта в промпт
   (vector / lexical → role boost).
3. Подготовка hints: `EnglishTutorTurnHints`, `SkillTurnHints`
   (кристаллизация + resolver `taskMatches`).
4. `BuildOutboundHermesPrompt` склеивает: системные инструкции +
   External Brain block + Skill resolver block + Skill generation rules +
   outbound prompt blocks включённых навыков + platform knowledge block.
5. `HermesService.SendMessageAsync` → `wsl -d <distro> -- /bin/bash -lc "… hermes chat …"`.
6. Разбор ответа: сначала skill JSON (`skill_save` / `run_generated`),
   потом flashcards/Reni/TTS, иначе обычный текст.
7. Post-process: запись в чат через UI Dispatcher, добавление в
   `_chatLogService`, `MemoryExtractorService.ExtractExperience`,
   `RoleExperienceCapture.TryCaptureAsync`, `SyncWslAgentMemoryToVault("after-chat")`,
   `SyncPlatformKnowledgeToVault("after-chat")`, `WslMemory.RefreshAsync`,
   `PublishAssistantTurnToSupabaseIfPossibleAsync`,
   `TrySaveHistoryAfterTurnAsync`.

---

## 9. Настройки (`HermesSettings`)

### External Brain / память

| Параметр | По умолчанию | Эффект |
|----------|--------------|--------|
| `ExternalBrainMemoryPath` | пусто | Корень vault (если не задан env / json overlay) |
| `ExternalBrainInjectIntoPrompt` | `true` | Подмешивать ли блок в `hermes chat` |
| `ExternalBrainMaxContextItems` | 12 | Сколько заметок в блок |
| `ExternalBrainVectorRetrievalEnabled` | `true` | Включать vector retrieval |
| `SyncWslAgentMemoryToExternalBrain` | `true` | Синк `USER.md`/`MEMORY.md` из WSL |
| `RoleAutoCapture` | `true` | Авто-захват в `Knowledge/<role>/` |
| `RoleAutoCaptureMinImportance` | 4 | Минимальный importance для авто-захвата |
| `RoleAutoCaptureMinLength` | 150 | Минимальная длина (problem + solution) |

### Skill generation

| Параметр | По умолчанию |
|----------|--------------|
| `SkillGenerationEnabled` | `true` |
| `SkillMirrorToWslHermes` | `true` |
| `GeneratedSkillsDirectory` | пусто → `%AppData%\HermesWpf\skills` |
| `SkillMaxGenerationAttempts` | 3 |
| `SkillRunTestsBeforeSave` | `true` |
| `SkillSandboxBeforeSave` | `true` |
| `SkillSandboxTimeoutSeconds` | 60 |
| `SkillAutoResolveForTasks` | `true` |
| `SkillResolveMaxSuggestions` | 3 |
| `SkillResolveMinScore` | 0.28 |

Файл настроек: `%AppData%\HermesWpf\settings.json`.

---

## 10. Сводная карта файлов

### Память

| Файл | Назначение |
|------|------------|
| `Hermes.Wpf/Services/ExternalBrainService.cs` | Vault, поиск, watcher, context block |
| `Hermes.Wpf/Services/MemoryVectorIndex.cs` | TF-IDF / Ollama embeddings, `SelectTopAsync` |
| `Hermes.Wpf/Services/OllamaEmbeddingClient.cs` | Прокси к Ollama API |
| `Hermes.Wpf/Services/MemoryExtractorService.cs` | Черновик опыта из чата |
| `Hermes.Wpf/Models/MemoryDraft.cs` | DTO черновика |
| `Hermes.Wpf/Services/RoleExperienceCapture.cs` | Авто-захват по роли |
| `Hermes.Wpf/Services/RoleManager.cs` | Текущая роль, алиасы |
| `Hermes.Wpf/Services/RoleAwareMemoryRouter.cs` | Бусты/штрафы по роли |
| `Hermes.Wpf/Services/RoleSkillIndex.cs` | Usage stats навыков |
| `Hermes.Wpf/Services/WslAgentMemorySyncService.cs` | WSL → vault |
| `Hermes.Wpf/Services/WslAgentMemoryPaths.cs` | UNC-пути WSL |
| `Hermes.Wpf/Services/HermesPlatformKnowledgeSyncService.cs` | Docs/Report → vault |
| `Hermes.Wpf/Services/VaultInitializer.cs` | Раскладка папок vault |
| `Hermes.Wpf/Views/MemoryEditorWindow.xaml.cs` | Ручное сохранение черновика |
| `Hermes.Wpf/ViewModels/ExternalBrainViewModel.cs` | UI vault |
| `Hermes.Wpf/ViewModels/WslMemoryViewModel.cs` | UI WSL memories |

### Навыки

| Файл | Назначение |
|------|------------|
| `Hermes.Wpf/Services/GeneratedSkillCatalogService.cs` | Загрузка манифестов, triggers, enable/disable |
| `Hermes.Wpf/Services/GeneratedSkillTaskMatcher.cs` | Resolver под задачу (TF-IDF + лексика) |
| `Hermes.Wpf/Services/GeneratedSkillRunner.cs` | Запуск `run.ps1`/`run.py`, smoke-test |
| `Hermes.Wpf/Services/GeneratedSkillPaths.cs` | Пути Windows + UNC к WSL |
| `Hermes.Wpf/Services/GeneratedSkillIndexService.cs` | Запись `index.json` |
| `Hermes.Wpf/Services/GeneratedSkillVaultSyncService.cs` | Экспорт в `Procedures/GeneratedSkills` |
| `Hermes.Wpf/Services/SkillGenerationService.cs` | Сохранение `skill_save` + sandbox + mirror |
| `Hermes.Wpf/Services/SkillSandboxService.cs` | Запуск под таймаутом перед save |
| `Hermes.Wpf/Services/SkillCrystallizeIntentParser.cs` | Парсер `skill_save`/`run_generated` JSON |
| `Hermes.Wpf/Services/SkillCrystallizeTriggers.cs` | Фразы пользователя для кристаллизации |
| `Hermes.Wpf/Services/SkillRunTriggers.cs` | «запусти навык …» |
| `Hermes.Wpf/Services/SkillReflectionService.cs` | Reflective prompt |
| `Hermes.Wpf/Services/SkillResolverInstructions.cs` | Текст блока resolver в промпт |
| `Hermes.Wpf/Services/SkillGenerationInstructions.cs` | Правила формирования skill_save |
| `Hermes.Wpf/Models/GeneratedSkillManifest.cs` | DTO манифеста |
| `Hermes.Wpf/ViewModels/GeneratedSkillsViewModel.cs` | UI каталога |

### Оркестрация

| Файл | Назначение |
|------|------------|
| `Hermes.Wpf/ViewModels/MainViewModel.cs` | Главный обработчик хода чата (см. §8) |
| `Hermes.Wpf/Views/MainWindow.xaml.cs` | Startup-sync (`vm.SyncWslAgentMemoryToVault("startup")`, `vm.SyncPlatformKnowledgeToVault("startup")`) |

---

## 11. Самообучение: что есть и чего нет

### Есть

- **Auto-captured procedural/semantic memory по ролям** — каждый успешный ответ,
  попадающий в фильтры `RoleExperienceCapture`, сразу превращается в `*.md`
  в `Knowledge/<role>/` и переиспользуется в следующих ходах через retrieval.
- **Usage learning по навыкам** — `RoleSkillIndex` помнит, какой навык в
  какой роли использовался, и поднимает его в `GetSkillsForRole`.
- **Кристаллизация навыка** — Hermes по фразе пользователя или по reflective
  prompt сам формирует `skill_save` JSON, который проходит sandbox и
  записывается как новый исполняемый артефакт.
- **Двусторонняя память** — `USER.md`/`MEMORY.md` из WSL CLI отзеркаливаются
  в `Identity/` и `Knowledge/`, попадая в тот же ретривер.
- **Platform self-doc** — отчёт по самой WPF-платформе всегда лежит в
  `Knowledge/Hermes/` и приходит в контекст.

### Нет (vs концепция Voyager/Gemini)

| Возможность | Статус |
|-------------|--------|
| Авто-создание навыка после **любой** успешной задачи без `skill_save` | нет |
| Docker-sandbox для script-навыков | нет (только temp + timeout + regex) |
| Полноценная векторная БД (Chroma/Qdrant) | нет (`MemoryVectorIndex` поверх vault) |
| `SOUL.md` self-edit (агент правит свой профиль) | нет |
| Единый plugin host `ISkill` (рантайм-плагины) | нет |
| Episodic store отдельно от vault (Redis) | нет |
| Auto-save `MemoryDraft` в vault для роли Universal | нет (нужна явная роль или ручной save) |

---

## 12. Логи, по которым видно работу контура

| Префикс | Значение |
|---------|----------|
| `[external-brain]` | Загрузка vault, watcher, debounce |
| `[vector-memory]` | Пересборка/запрос индекса (TF-IDF или Ollama) |
| `[role-capture]` | Авто-захват в `Knowledge/<role>/` |
| `[wsl-memory-sync]` | Экспорт `USER.md`/`MEMORY.md` |
| `[platform-knowledge]` | Экспорт отчёта в vault |
| `[skill-resolver]` | Подбор навыка под задачу |
| `[skill-gen]` | Кристаллизация и запись |
| `[skill-sandbox]` | Прогон script перед save |
| `[skill-index]` | `index.json` |
| `[skill-vault]` | Экспорт навыка в `Procedures/GeneratedSkills` |
| `[skill-run]` | stdout/stderr запущенного навыка |
| `[role-manager]` / `[role-skill]` | Смена роли, usage stats навыков |

---

## 13. Кратко в одной таблице

| Шаг | Кто пишет | Что появляется |
|-----|-----------|----------------|
| Пользователь спрашивает в чате | — | новая запись в `ChatLogService`, история |
| Готов ответ Hermes | `MemoryExtractorService` | `MemoryDraft` в RAM |
| Активна роль ≠ Universal, importance ≥ 4 | `RoleExperienceCapture` | `*.md` в `Knowledge/<role>/` (procedural/semantic) |
| Завершён ход чата | `WslAgentMemorySyncService` | `Identity/WslAgent_USER.md`, `Knowledge/WslAgent_MEMORY.md` (если изменились) |
| Завершён ход чата | `HermesPlatformKnowledgeSyncService` | `Knowledge/Hermes/<отчёт>.md` |
| Пользователь сказал «сохрани как навык» | `SkillGenerationService` | `skills/<id>/{manifest.json, SKILL.md, run.*}`, vault `Procedures/GeneratedSkills/Skill_<id>.md`, опционально `~/.hermes/skills/<id>/` |
| Триггер совпал | `GeneratedSkillRunner` | `[skill-run]` лог, ответ в чат |
| Сменилась роль | `RoleManager` + `RoleSkillIndex.LoadAsync` | пересортировка skill index |

---

*Конец отчёта.*

---

## Source: `Trading_Platform_Learning_Touchpoints.md`

# Точки контакта Hermes.TradingPlatform с обучающим контуром

**Дата:** 2026-05-25  
**Область:** только проект `Hermes.TradingPlatform.*` и его мост в Hermes.Wpf.  
**Связанные документы:**
[`Experience_Learning_Skills.md`](Experience_Learning_Skills.md),
[`Docs/Report/Hermes_Trading_Platform_Integration.md`](../Docs/Report/Hermes_Trading_Platform_Integration.md),
[`Docs/Report/Experience_And_Skills_Logic_Report.md`](../Docs/Report/Experience_And_Skills_Logic_Report.md).

---

## 1. Архитектура: что трейдинг даёт обучению

Hermes.TradingPlatform — **отдельный WPF-процесс**, у него нет прямого
доступа к External Brain. Учится платформа двумя путями:

```mermaid
flowchart LR
  subgraph trading [Hermes.TradingPlatform.Wpf]
    UI[Pages: Positions/Orders/RiskManager/Journal/Assistant]
    Host[TradingPlatformHost]
    Engine[VirtualExchangeEngine]
    Journal[TradeJournalFileWriter\ntrade_journal.jsonl]
    Feed[BinanceFuturesMarketDataFeed]
    MiniAsst[MiniAssistantViewModel\n+ AppAssistantService OpenRouter]
    CtxProv[TradingInAppAssistantContextProvider]
  end

  subgraph bridge [Bridge]
    Snap[TradingPlatformSnapshotFile]
    Cmd[TradingPlatformCommandFile]
  end

  subgraph wpf [Hermes.Wpf]
    Main[MainViewModel]
    Brain[ExternalBrainService]
    Capture[RoleExperienceCapture Trader]
  end

  Feed --> Engine
  Engine --> Journal
  Engine --> Snap
  Cmd --> Engine
  Host --> Snap
  Host --> Cmd
  Main <--> Snap
  Main <--> Cmd
  Main --> Capture --> Brain
  UI --> CtxProv --> MiniAsst
  MiniAsst -- OpenRouter --> Cloud[(LLM)]
```

| Канал | Куда учим | Кто пишет |
|-------|-----------|-----------|
| **Trade journal** | `%LocalAppData%\HermesTrading\trade_journal.jsonl` — append-only фактический лог сделок | `TradeJournalFileWriter` |
| **Bridge snapshot/command** | JSON-файлы для Hermes.Wpf, чтобы видеть состояние и принимать `manual_order`, `close_position` | `TradingPlatformBridgePublisher` ↔ `TradingPlatformBridgeService` |
| **Чат с Hermes** (в обычном WPF) | RoleExperienceCapture(role=Trader) → vault `Knowledge/Trading/` | `Hermes.Wpf.MainViewModel` |
| **In-app ассистент** | OpenRouter (`Hermes.InAppAssistant`) — отвечает по live-snapshot, **не пишет** в vault | `MiniAssistantViewModel` |

---

## 2. Журнал сделок как первичный источник опыта

### 2.1. Файл и формат

`Hermes.TradingPlatform.Data.Persistence.TradeJournalFileWriter`

- Путь: `%LocalAppData%\HermesTrading\trade_journal.jsonl`.
- Формат: одна сделка — одна строка JSON (`TradeJournalEntry`):

| Поле | Описание |
|------|----------|
| `Id`, `Timestamp` | UUID и UTC-timestamp |
| `OrderId`, `Symbol`, `Side` | Привязка к ордеру и инструменту |
| `Kind` | `Open` / `Add` / `Reduce` / `Close` |
| `Quantity`, `FillPrice`, `Fee` | Исполнение |
| `RealizedPnl`, `BalanceBefore`, `BalanceAfter` | Денежный эффект |
| `ReduceOnly` | Помечает SL/TP/Reduce-only fills |

Запись синхронная, под `lock`, поток поддерживает дозапись (`Append`),
полную загрузку (`LoadAll`) и очистку (`Clear`). Кроме file-writer
есть `SqliteJournalStore` (`IJournalStore`-родственник).

### 2.2. Что это даёт обучению

- **Replay**: `JournalReplayService` + `ReplayViewModel` подставляют исторические
  сделки в paper-движок для проверки гипотез/стратегий.
- **Аналитика в UI**: `JournalViewModel` показывает все строки с фильтрами,
  RealizedPnL и комиссиями.
- **Экспорт в External Brain** делается **не напрямую** — через ход чата в
  Hermes.Wpf: пользователь обсуждает результат торговли в режиме `Trader`,
  `RoleExperienceCapture` фиксирует `procedural`/`semantic` заметку в
  `Knowledge/Trading/` (см. §4).

---

## 3. Risk Profile и авто-обучение исполнению

`Hermes.TradingPlatform.Core/Domain/RiskProfile.cs` (+
`RiskProfileSettingsDto.cs`, `RiskProfileFileStore.cs`).

Платформа теперь умеет «учиться» исполнять сделки безопасно по умолчанию:

| Поле | Назначение |
|------|------------|
| `RiskPerTradePercent` | Целевой risk % на сделку (используется для SL) |
| `DefaultTakeProfitRrMultiplier` | TP-distance = TP × SL-distance (RR) |
| `AutoApplyDefaultSlTp` | Если `true`, после открытия позиции движок прикручивает SL+TP |
| `MaxExposurePercent`, `SafeMode`, `AutoShutdown`, `EmergencyHalt` | Защитные правила |

`VirtualExchangeEngine.TryAttachDefaultSlTp` (вызывается после `FillOrder`):

1. Считает SL-distance от `RiskPerTradePercent`;
2. Создаёт reduce-only Stop-ордер (SL) и Limit-ордер (TP) c
   `Purpose ∈ {Entry, SL, TP, Reduce}`;
3. Эти ордера попадают в `OrderDto.Purpose` и в `PositionDto.StopLossPrice`,
   `PositionDto.TakeProfitPrice` через
   `TradingUiMapper.ToDto(Position, IReadOnlyList<Order>)`.

UI Risk Manager (`RiskManagerView.xaml`, `RiskManagerViewModel.cs`) даёт
редактировать TP multiplier и переключатель «Auto SL/TP при открытии».

### 3.1. Источник «опыта» — Risk validator + журнал

`IRiskValidator` (см. `RiskValidator`) ловит превышения и пишет события в
`TradeUiFeedback` и `EventLogProjection`. Эти события:

- видны в чате Hermes.Wpf через bridge snapshot,
- попадают в обсуждение, и через `RoleExperienceCapture(role=Trader)` могут
  стать `Knowledge/Trading/*.md`, если пользователь подтвердит выводы.

---

## 4. Hermes.Wpf как обучающий центр для Trader-режима

В Hermes.Wpf пользователь говорит «трейдинг»/`trading` →
`RoleManager.TrySwitchRoleFromMessage` переключает роль на `Trader`.
Что меняется:

- `RoleAwareMemoryRouter` бустит ноты с тегами
  `trading|market|strategy|pnl|position|order|risk` и пути
  `Knowledge/Trading/*`, `Procedures/Trading/*`, `Projects/Trading/*`.
- `RoleAwareMemoryRouter` штрафует `english|vocabulary|grammar`.
- `RoleExperienceCapture` начинает писать `procedural`/`semantic` заметки
  в `Knowledge/Trading/` (тег `trading`, `auto-captured`).
- `MainViewModel.ExecuteHermesUserTurnAsync` первыми пробует
  `TryHandleManualOrderLocalAsync` и `TryHandleClosePositionLocalAsync`,
  чтобы команда «открой лонг по биткоину» не уходила в desktop vision
  (см. историю чата 2026-05).
- Команды уходят на платформу через `TradingPlatformBridgeService` (файлы
  `TradingPlatformCommandFile`/`TradingPlatformSnapshotFile`), а результат
  («[trading-bridge] manual_order ok=… detail=…») пишется в `ChatLogService`
  и **именно эта пара (запрос пользователя → детализированный ответ
  системы) и есть кандидат на сохранение в `Knowledge/Trading/`** —
  фильтры `MemoryExtractorService.ScoreImportance` + `RoleAutoCaptureMinLength`
  обычно пропускают такие записи как `procedural`.

---

## 5. In-app ассистент (OpenRouter) — отдельный контур

`Hermes.TradingPlatform.Wpf/ViewModels/Shell/MainViewModel.cs`:

```csharp
var assistantContext = new TradingInAppAssistantContextProvider(() => this);
InAppAssistant = new MiniAssistantViewModel(
    new AppAssistantService(logger: new TradingAppAssistantLogger()),
    () =>
    {
        var s = _host.PlatformSettingsStore.Load();
        return new AppAssistantOptions
        {
            ApplicationId = AppAssistantKnowledge.TradingPlatformId,
            OpenRouterApiKey = s.InAppAssistantOpenRouterApiKey,
            Model = s.InAppAssistantOpenRouterModel,
        };
    },
    assistantContext);
```

| Компонент | Что делает |
|-----------|------------|
| `Hermes.InAppAssistant/AppAssistantService.cs` | Прямой вызов OpenRouter chat completions |
| `Hermes.InAppAssistant/AppAssistantKnowledge.cs` | Системные промпты для `hermes-wpf` и `hermes-trading-platform` |
| `TradingInAppAssistantContextProvider` | Снимок состояния (страница, account, PnL, OpenPositionsCount, статус Hermes orchestration) для system prompt |
| `TradingAppAssistantLogger` | Перенаправляет логи ассистента в `TradingPlatformFileLogger` |

Это **отдельный** от External Brain канал. Он:

- не пишет в vault и не запускает skills;
- ограничен «in-app helper only — you do not execute trades»;
- получает live snapshot платформы как факт «прямо сейчас».

Самообучения в этом контуре **нет** — это retrieval-augmented helper
с фиксированной базой знаний (`HermesWpfDoc` / `TradingPlatformDoc` в
`AppAssistantKnowledge`).

---

## 6. Market data feed — диагностика и самонастройка

`Hermes.TradingPlatform.Exchange/MarketData/BinanceFuturesMarketDataFeed.cs`

В мае 2026 фид перестроили на гибридную схему:

1. **WebSocket `/ws`** — `SUBSCRIBE` к `@bookTicker` (mid-цена) и `@ticker` (24h).
2. **REST poller `Poll24hrStatsLoopAsync`** — раз в минуту дотягивает
   `ChangePercent24h` и `QuoteVolume24h` из `/fapi/v1/ticker/24hr`,
   если WS-`@ticker` не пришёл.
3. `_diagnosticLog` — расширенное логирование (первый тик, ошибки, состояние
   подписки) через `TradingPlatformFileLogger`.

Это **операционное** обучение — фид сам выбирает наиболее надёжный источник,
не записывая в External Brain. Однако диагностические сообщения попадают в
`Logs` UI и могут обсуждаться в чате Hermes.Wpf, что снова замыкается на
`RoleExperienceCapture` для роли Trader.

---

## 7. Сводная таблица: где «учится» трейдинг-платформа

| Уровень | Артефакт | Файл | Как используется |
|---------|----------|------|------------------|
| Сделки | `trade_journal.jsonl` | `TradeJournalFileWriter` | Replay, Journal UI, аналитика PnL |
| Состояние | `session_state.json` | `TradingSessionStateFileStore` | Восстановление позиций/ордеров после перезапуска |
| Snapshot для Hermes | `bridge\snapshot.json` | `TradingPlatformSnapshotFile` | Hermes.Wpf видит платформу |
| Команды от Hermes | `bridge\command.json` | `TradingPlatformCommandFile` | manual_order, close_position |
| Risk-profile | `risk_profile.json` | `RiskProfileFileStore` | SL/TP defaults, лимиты |
| Settings | `platform_settings.json` | `PlatformSettingsFileStore` | Источник котировок, OpenRouter API key |
| Логи | `TradingPlatformFileLogger` сессионные файлы | `Logs` UI | Диагностика, обсуждаются в Hermes.Wpf |

---

## 8. Самообучение в трейдинге: что есть и чего нет

### Есть

- **Auto SL/TP** — движок сам прикручивает защитные ордера по rules из
  Risk Manager (см. §3).
- **Auto-capture** для Trader-роли в Hermes.Wpf (`RoleExperienceCapture` →
  `Knowledge/Trading/`).
- **Replay** на исторических данных журнала.
- **Hermes-bridge**: команды чата уходят в платформу, результаты возвращаются
  и становятся материалом для memory extractor.
- **Hybrid Binance feed** — REST дополняет WebSocket, без потери данных при
  частичных потерях стрима.

### Нет

| Возможность | Статус |
|-------------|--------|
| Auto-generate trading skill после серии прибыльных сделок | нет (только ручная кристаллизация в Hermes.Wpf) |
| Reinforcement learning над `trade_journal.jsonl` | нет |
| Прямая запись из платформы в External Brain | нет (через bridge → Hermes.Wpf → Capture) |
| Self-edit для RiskProfile | нет (только пользователь через UI) |
| Online-tuning стратегий | нет (стратегии конфигурируются вручную) |

---

## 9. Где смотреть в логах

| Источник | Префиксы |
|----------|----------|
| Hermes.TradingPlatform (`TradingPlatformFileLogger`) | `[exchange]`, `[market-data]`, `[bridge]`, `[risk]`, `[journal]`, `[assistant]` |
| Hermes.Wpf при работе с трейдингом | `[trading-bridge]`, `[role-manager]`, `[role-capture]`, `[external-brain]` |
| OpenRouter ассистент | `[openrouter-assistant]` |

---

## 10. Резюме

- Платформа **сама не пишет** в External Brain; она генерит факты (журнал,
  риск-события, market data), которые становятся учебным материалом, когда
  пользователь обсуждает их в чате Hermes.Wpf в роли `Trader`.
- Учебный цикл закрывается через мост и через `RoleExperienceCapture`.
- Внутри платформы есть точечное «само-исполнение» (Auto SL/TP, REST fallback
  для котировок), но это операционные правила, а не обучение в смысле
  External Brain.
- In-app ассистент — самостоятельный helper, не часть обучения.

---

*Конец отчёта.*

---

## Source: `Connection_Log_Report_2026-04-29.md`

# Connection Log Report (2026-04-29)

## Проверенные логи

- `Docs/Logs/hermes_session_20260429_134601.log`
- `Docs/Logs/hermes_session_20260429_133540.log`

## Наблюдаемая проблема

В текущих логах повторяется одна и та же ошибка подключения:

- `WSL_E_DISTRO_NOT_FOUND`
- `There is no distribution with the supplied name.`

Типичные строки:

```text
2026-04-29 13:46:02.392 [ERROR] [connection] Venv failed: ... WSL_E_DISTRO_NOT_FOUND
2026-04-29 13:46:20.477 [ERROR] [connection] Venv failed: ... WSL_E_DISTRO_NOT_FOUND
```

## Что это означает

Ошибка возникает при вызове `wsl.exe -d "<WslDistro>" ...`, когда WSL не находит переданное имя дистрибутива.

Важный момент: сообщение приходит на шаге `Venv failed`, но первопричина именно в `distro not found`, а не в venv.

## Вероятная причина

- В активной сессии приложения используется некорректное/устаревшее значение `WslDistro` (или продолжает работать старый экземпляр с прежними настройками).
- Из-за `AutoReconnect=true` watchdog повторяет проверку и пишет ту же ошибку циклически.

## Проверка окружения вручную (успешная)

Ручные команды в PowerShell ранее прошли:

- `wsl -d ubuntu -- /bin/bash -lc "echo ok"` -> `ok`
- `wsl -d ubuntu -- /bin/bash -lc "source ~/hermes-agent/venv/bin/activate && hermes status"` -> успешный статус Hermes

Это подтверждает, что WSL+Hermes рабочие, а проблема локализуется в значении/применении `WslDistro` внутри запущенного приложения.

## Рекомендованные действия

1. Закрыть все процессы `Hermes.Wpf`.
2. Запустить приложение заново.
3. Проверить `Settings`:
   - `WSL Distro = Ubuntu`
   - `Venv Path = ~/hermes-agent/venv`
   - `Hermes Command = hermes`
4. Нажать `Reconnect`.
5. Повторно проверить новый лог: ошибка `WSL_E_DISTRO_NOT_FOUND` должна исчезнуть.

---

## Source: `Project_Structure.md`

# Hermes.Wpf Project Structure (Context)

Ниже структура, важная для понимания контекста подключения и логов.

```text
Hermes.Wpf/
  Models/
    ChatMessage.cs
    HermesProject.cs
    SessionHistory.cs
    HermesSettings.cs
    ConnectionState.cs
    ConnectionStatus.cs

  Services/
    HermesService.cs
    ConnectionService.cs
    SettingsService.cs
    ProjectService.cs
    HistoryService.cs
    LogService.cs

  ViewModels/
    MainViewModel.cs
    SetupWizardViewModel.cs
    SettingsViewModel.cs
    ChatViewModel.cs
    ProjectViewModel.cs
    BaseViewModel.cs

  Views/
    MainWindow.xaml
    StatusIndicator.xaml
    SetupWizardWindow.xaml
    SettingsWindow.xaml
    ChatView.xaml
    ProjectPanel.xaml
    TerminalView.xaml
    LogsWindow.xaml
    HelpWindow.xaml

  Converters/
    ConnectionStateToColorConverter.cs

  Commands/
    RelayCommand.cs
```

Логи приложения:

```text
Docs/Logs/hermes_session_*.log
```

---

## Source: `Code_Context.md`

# Code Context for Connection Diagnostics

## 1) ConnectionService (`Services/ConnectionService.cs`)

Ключевые моменты:
- Preflight начинается с `wsl.exe --status`.
- Затем идут проверки `venv`, `Hermes CLI`, `hermes status`.
- Все bash-команды запускаются через `-- /bin/bash -lc` и `-d "<WslDistro>"`.

Основной фрагмент:

```csharp
private static string BuildWslArgs(HermesSettings settings, string bashCommand)
{
    var escaped = bashCommand.Replace("\"", "\\\"");
    if (!string.IsNullOrWhiteSpace(settings.WslDistro))
    {
        return $"-d \"{settings.WslDistro}\" -- /bin/bash -lc \"{escaped}\"";
    }

    return $"-- /bin/bash -lc \"{escaped}\"";
}
```

## 2) HermesService (`Services/HermesService.cs`)

Ключевые моменты:
- Chat/quick actions используют тот же надежный шаблон WSL-вызова.
- Добавляется `cd '<wslWorkDir>'` при наличии project context.

Основной фрагмент:

```csharp
private static string BuildWslArgs(HermesSettings settings, string bashCommand, string? wslWorkDir = null)
{
    var cdPrefix = string.IsNullOrWhiteSpace(wslWorkDir) ? string.Empty : $"cd '{wslWorkDir}' && ";
    var fullCmd = $"{cdPrefix}{bashCommand}";
    var escaped = fullCmd.Replace("\"", "\\\"");

    if (!string.IsNullOrWhiteSpace(settings.WslDistro))
    {
        return $"-d \"{settings.WslDistro}\" -- /bin/bash -lc \"{escaped}\"";
    }

    return $"-- /bin/bash -lc \"{escaped}\"";
}
```

## 3) MainViewModel (`ViewModels/MainViewModel.cs`)

Ключевые моменты:
- `ReconnectCommand` запускает `RefreshConnectionAsync()`.
- Watchdog таймер повторяет reconnect при `AutoReconnect=true`.
- Именно поэтому ошибка в логах повторяется циклически.

Основной фрагмент:

```csharp
_watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
_watchdogTimer.Tick += async (_, _) => await WatchdogTickAsync();
_watchdogTimer.Start();
```

## 4) Settings (`Models/HermesSettings.cs` + `Views/SettingsWindow.xaml`)

Ключевые параметры:
- `WslDistro`
- `VenvPath`
- `HermesCommand`
- `ChatTimeoutSeconds`
- `AutoReconnect`

Для текущей ошибки критичен `WslDistro`.

