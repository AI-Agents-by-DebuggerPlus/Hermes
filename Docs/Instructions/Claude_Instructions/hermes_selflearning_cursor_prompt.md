# Cursor Prompt: Role-Aware Self-Learning System for Hermes.Wpf

## Контекст проекта

Ты работаешь над **Hermes.Wpf** — WPF-приложением, которое является фронтендом для AI-агента Hermes (запускается через WSL `hermes chat`). Проект расположен в `D:\Programming\AI_Agents\Hermes`.

Ключевые уже реализованные подсистемы:
- **ExternalBrainService** — Markdown vault (Obsidian-стиль), FileSystemWatcher, TF-IDF + Ollama vector retrieval
- **MemoryExtractorService** — извлечение черновика опыта после каждого ответа (без автосохранения)
- **GeneratedSkillCatalogService / SkillGenerationService** — каталог навыков, кристаллизация через `skill_save` JSON
- **GeneratedSkillTaskMatcher** — TF-IDF + лексический resolver для подбора существующих навыков
- **TradingPlatformBridgeService** — file-bridge с терминалом (snapshot.json, commands.json)
- **WslAgentMemorySyncService** — экспорт USER.md / MEMORY.md из WSL в vault
- **HermesPlatformKnowledgeSyncService** — синхронизация справки о платформе в vault
- **MainViewModel** — оркестрация: BuildContextAsync → BuildOutboundHermesPrompt → hermes chat → post-process

Агент поддерживает роли (частично реализованы): Universal, Developer, Trader, EnglishTutor, PersonalManager.

Файл настроек: `%AppData%\HermesWpf\settings.json` (класс `HermesSettings`).

---

## Цель задачи

Реализовать **ролевую систему самообучения**: каждая роль накапливает собственный контекст опыта и навыков, и при активации роли агент мгновенно получает релевантный контекст — без ручного поиска. Пользователь переключает роль → промпт автоматически содержит нужные знания и навыки.

---

## Задача 1: RoleAwareMemoryRouter — маршрутизация памяти по ролям

### Что нужно создать

**Новый сервис:** `Hermes.Wpf/Services/RoleAwareMemoryRouter.cs`

Логика: при вызове `BuildContextAsync` учитывать активную роль и приоритизировать записи vault, теги и навыки, соответствующие этой роли.

```csharp
public enum AgentRole
{
    Universal,
    Developer,    // включает Computer Operator
    Trader,
    EnglishTutor,
    PersonalManager
}

public class RoleAwareMemoryRouter
{
    // Маппинг роль → теги vault, которые получают буст при scoring
    private static readonly Dictionary<AgentRole, string[]> RolePrimaryTags = new()
    {
        [AgentRole.Trader]         = new[] { "trading", "market", "strategy", "pnl", "position", "order", "risk" },
        [AgentRole.Developer]      = new[] { "dotnet", "csharp", "code", "wpf", "wsl", "git", "debug", "architecture" },
        [AgentRole.EnglishTutor]   = new[] { "english", "vocabulary", "grammar", "exercise", "pronunciation" },
        [AgentRole.PersonalManager]= new[] { "task", "project", "goal", "productivity", "deadline", "habit" },
        [AgentRole.Universal]      = Array.Empty<string>()
    };

    // Маппинг роль → подпапки vault с повышенным приоритетом
    private static readonly Dictionary<AgentRole, string[]> RoleVaultPaths = new()
    {
        [AgentRole.Trader]         = new[] { "Knowledge/Trading", "Procedures/Trading", "Projects/Trading" },
        [AgentRole.Developer]      = new[] { "Knowledge/Development", "Procedures/Dev", "Projects" },
        [AgentRole.EnglishTutor]   = new[] { "Knowledge/English", "Procedures/English" },
        [AgentRole.PersonalManager]= new[] { "Knowledge/Productivity", "Projects", "Identity" },
        [AgentRole.Universal]      = Array.Empty<string>()
    };

    public AgentRole CurrentRole { get; set; } = AgentRole.Universal;

    // Возвращает отфильтрованные и ранжированные MemoryItem с учётом роли
    public IReadOnlyList<MemoryItem> FilterAndBoost(
        IReadOnlyList<MemoryItem> allItems,
        string userQuery,
        int maxItems);

    // Тег роли для vault-записей создаваемых в этой роли
    public string GetRoleTag(AgentRole role);
}
```

**Алгоритм FilterAndBoost:**
1. Базовый TF-IDF скоринг по `userQuery` (использовать существующий `ScoreMemories` из ExternalBrainService).
2. Применить **role boost** (+0.3 к score) для записей, у которых:
   - теги содержат любой тег из `RolePrimaryTags[CurrentRole]`, ИЛИ
   - `SourceFile` содержит любой путь из `RoleVaultPaths[CurrentRole]`.
3. Применить **role penalty** (×0.4 к score) для записей, у которых теги другой специфической роли (например, в режиме Trader записи с тегом `english` получают penalty).
4. Сортировка по итоговому score DESC + recency tiebreak.
5. Вернуть top `maxItems`.

---

## Задача 2: RoleSkillIndex — ролевой индекс навыков

### Что нужно создать

**Новый сервис:** `Hermes.Wpf/Services/RoleSkillIndex.cs`

```csharp
public class RoleSkillIndex
{
    // При загрузке каталога — разбить навыки по ролям
    public void Rebuild(IReadOnlyList<GeneratedSkillManifest> allSkills);

    // Вернуть навыки, относящиеся к роли, отсортированные по частоте использования
    public IReadOnlyList<GeneratedSkillManifest> GetSkillsForRole(AgentRole role, int maxItems = 10);

    // Записать факт использования навыка (инкремент счётчика)
    public void RecordUsage(string skillId, AgentRole activeRole);

    // Персистентность: %AppData%\HermesWpf\role-skill-index.json
    public Task SaveAsync();
    public Task LoadAsync();
}
```

**Маппинг навыков по ролям** — определяется из `manifest.json` навыка:
- Добавить в `manifest.json` поле `"roles": ["Trader", "Developer"]` (опционально; если отсутствует — Universal).
- При `Rebuild` навыки без поля `roles` или с `["Universal"]` попадают во все роли.
- `GeneratedSkillTaskMatcher.Rank()` вызывать только по навыкам активной роли (+ Universal) для ускорения и повышения точности.

**Обновить `GeneratedSkillTaskMatcher`:**
```csharp
// Добавить параметр roleFilter
public IReadOnlyList<SkillMatch> Rank(
    string userTask,
    AgentRole activeRole,        // новый параметр
    int maxItems = 3,
    double minScore = 0.28);
```
Внутри — использовать `RoleSkillIndex.GetSkillsForRole(activeRole)` как корпус вместо всего каталога.

---

## Задача 3: RoleContextBlock — ролевой блок в промпте

### Что нужно создать

**Новый сервис:** `Hermes.Wpf/Services/RoleContextBlockService.cs`

```csharp
public class RoleContextBlockService
{
    // Формирует блок для подмешивания в BuildOutboundHermesPrompt
    public string BuildRoleContextBlock(AgentRole role, RoleSession session);
}
```

**RoleSession** — in-memory состояние текущей сессии роли:
```csharp
public class RoleSession
{
    public AgentRole Role { get; init; }
    public DateTime StartedAt { get; init; }
    public int TurnCount { get; set; }
    public List<string> RecentSkillIds { get; } = new();   // последние N использованных навыков
    public List<string> RecentTopics { get; } = new();     // темы последних сообщений (auto-extract)
}
```

**Формат блока в промпте** (пример для Trader):
```
--- ROLE CONTEXT: Trader ---
Active since: 14:32 (12 turns)
Recent skills used: manage_stop_loss, analyze_position
Recent topics: BTCUSDT momentum, risk sizing
Relevant memory loaded: 8 items (tags: trading, strategy, risk)
---
```

**Интеграция в `MainViewModel.BuildOutboundHermesPrompt`:**
```csharp
// Добавить после блока External Brain, перед Skill Resolver
if (_roleContextBlock.IsEnabled)
{
    outbound.AppendLine(_roleContextBlockService.BuildRoleContextBlock(
        _roleManager.CurrentRole, _roleSession));
}
```

---

## Задача 4: RoleExperienceCapture — автосохранение опыта по роли

### Проблема

Сейчас `MemoryExtractorService` создаёт черновик после каждого ответа, но **не сохраняет автоматически**. Пользователь должен нажать «Save experience» вручную. Это приводит к потере ценного контекста.

### Решение

**Новый сервис:** `Hermes.Wpf/Services/RoleExperienceCapture.cs`

```csharp
public class RoleExperienceCapture
{
    // Вызывается из MainViewModel после каждого успешного хода
    public Task<bool> TryCaptureAsync(
        MemoryDraft draft,
        AgentRole activeRole,
        string vaultPath);
}
```

**Логика автосохранения** (вызывать ТОЛЬКО если все условия выполнены):

| Условие | Пороговое значение |
|---|---|
| `draft.Importance` | >= 4 |
| `draft.Content.Length` | >= 150 символов |
| `draft.Type` | `procedural` или `semantic` |
| Роль | не `Universal` (Universal требует ручного сохранения) |
| Дубликат | SHA-256 первых 200 символов не совпадает с последними 50 автосохранёнными |

**При автосохранении:**
1. Добавить тег роли (`trading`, `development`, `english`, `productivity`) в frontmatter.
2. Добавить тег `#auto-captured`.
3. Сохранить в подпапку роли: `Knowledge/Trading/`, `Knowledge/Development/` и т.д.
4. Лог: `[role-capture] Auto-saved {type} memory for role {role}: {title}`

**Обновить `HermesSettings`:**
```json
"RoleAutoCapture": true,
"RoleAutoCaptureMinImportance": 4,
"RoleAutoCaptureMinLength": 150
```

---

## Задача 5: Post-Trade Memory Export — опыт из торговых сделок

### Проблема

`trade_journal.jsonl` никогда не попадает в External Brain. После ошибки риска, emergency stop или крупного PnL — ничего не сохраняется.

### Что нужно создать

**Новый сервис:** `Hermes.Wpf/Services/TradingExperienceExporter.cs`

```csharp
public class TradingExperienceExporter
{
    // Подписывается на события bridge
    public void AttachToBridge(TradingPlatformBridgeService bridge);

    // Вызывается при получении значимого события из snapshot diff
    private Task OnSignificantTradeEventAsync(TradeEvent evt);
}

public enum TradeEventKind
{
    LargeRealizedPnl,        // |PnL| > порог (настраивается)
    RiskRejection,           // команда отклонена RiskValidator
    EmergencyStop,           // action=emergency_stop
    StrategyEnabled,         // включена/выключена стратегия
    DrawdownThreshold        // equity упала на X% от пика
}
```

**При каждом значимом событии:**
1. Создать Markdown-файл в `Knowledge/Trading/Episodes/`.
2. Шаблон записи:
```markdown
---
type: episodic
role: Trader
tags: [trading, episode, {event_kind}]
importance: {3 или 4 в зависимости от типа}
captured: auto
date: {ISO datetime}
---

# {EventKind}: {Symbol} {Side} {Quantity}

**Event:** {описание события}
**Context:** Balance={balance}, Equity={equity}, Open positions={count}
**Outcome:** {realized_pnl или rejection_reason или "Emergency stop triggered"}
**Active strategies:** {список}

## Lesson prompt
<!-- Hermes заполнит при следующем обращении в режиме Trader -->
```
3. Лог: `[trading-experience] Captured {event_kind} episode: {filename}`
4. После записи вызвать `ExternalBrainService.RestartWatcherAndReload("trading-experience")`.

**Diff-мониторинг snapshot:**
- Сравнивать предыдущий и текущий `snapshot.json` при каждом обновлении bridge.
- Хранить `_previousSnapshot` в сервисе.
- Определять `TradeEventKind` по diff.

**Обновить `HermesSettings`:**
```json
"TradingExperienceExportEnabled": true,
"TradingExperiencePnlThreshold": 50.0,
"TradingExperienceDrawdownThreshold": 0.05
```

---

## Задача 6: RoleManager — управление переключением ролей

### Что нужно создать

**Новый сервис:** `Hermes.Wpf/Services/RoleManager.cs`

```csharp
public class RoleManager
{
    public AgentRole CurrentRole { get; private set; } = AgentRole.Universal;
    public event EventHandler<RoleChangedEventArgs> RoleChanged;

    public void SwitchRole(AgentRole newRole);
    public void SwitchRole(string roleNameOrAlias);  // "трейдинг", "trader", "dev", etc.

    // Сохранять последнюю роль в settings (persist)
    public Task SaveCurrentRoleAsync();
    public Task LoadCurrentRoleAsync();
}
```

**Алиасы для ролей** (регистронезависимые):
```csharp
private static readonly Dictionary<string, AgentRole> Aliases = new(StringComparer.OrdinalIgnoreCase)
{
    ["trader"]          = AgentRole.Trader,
    ["trading"]         = AgentRole.Trader,
    ["трейдинг"]        = AgentRole.Trader,
    ["трейдер"]         = AgentRole.Trader,
    ["dev"]             = AgentRole.Developer,
    ["developer"]       = AgentRole.Developer,
    ["разработчик"]     = AgentRole.Developer,
    ["код"]             = AgentRole.Developer,
    ["english"]         = AgentRole.EnglishTutor,
    ["английский"]      = AgentRole.EnglishTutor,
    ["репетитор"]       = AgentRole.EnglishTutor,
    ["manager"]         = AgentRole.PersonalManager,
    ["productivity"]    = AgentRole.PersonalManager,
    ["эффективность"]   = AgentRole.PersonalManager,
    ["задачи"]          = AgentRole.PersonalManager,
    ["universal"]       = AgentRole.Universal,
    ["общий"]           = AgentRole.Universal,
};
```

**При переключении роли** (`SwitchRole`):
1. Установить `CurrentRole`.
2. Вызвать `RoleAwareMemoryRouter.CurrentRole = newRole`.
3. Вызвать `RoleSkillIndex.GetSkillsForRole(newRole)` для прогрева.
4. Создать новый `RoleSession`.
5. Вызвать `_roleChanged` event → MainViewModel обновляет UI и промпт.
6. Лог: `[role-manager] Switched to {newRole}`.

**Интеграция в `MainViewModel`:**
- При получении ответа Hermes проверять через `TradingModeIntentParser`-аналог, не содержит ли ответ смену роли.
- Существующая логика `TradingModeEnabled` → делегировать в `RoleManager.SwitchRole("Trader")`.

---

## Задача 7: Vault Structure — структура папок vault

Обеспечить создание структуры при первом запуске (`ExternalBrainService` или отдельный `VaultInitializer`):

```
{vault}/
├── Identity/
├── Knowledge/
│   ├── Trading/
│   │   └── Episodes/
│   ├── Development/
│   ├── English/
│   ├── Productivity/
│   └── Hermes/
├── Procedures/
│   ├── Trading/
│   ├── Dev/
│   ├── English/
│   └── GeneratedSkills/
└── Projects/
```

Для каждой роли создать `README.md` с описанием назначения папки.

---

## Задача 8: UI — Role Switcher

### Обновления UI

**В `MainWindow.xaml`** — добавить ролевой индикатор рядом со StatusIndicator:

```xml
<!-- Role Switcher Control -->
<ComboBox x:Name="RoleSwitcher"
          ItemsSource="{Binding AvailableRoles}"
          SelectedItem="{Binding CurrentRole}"
          Width="160"
          ToolTip="Active agent role"/>
```

**Цвет индикатора по роли:**

| Роль | Цвет |
|---|---|
| Universal | #808080 (серый) |
| Developer | #4A9EFF (синий) |
| Trader | #4CAF50 (зелёный) |
| EnglishTutor | #FF9800 (оранжевый) |
| PersonalManager | #9C27B0 (фиолетовый) |

**В `SettingsWindow`** добавить секцию «Role Memory»:
- Переключатель `RoleAutoCapture` (вкл/выкл автосохранения опыта)
- Slider `RoleAutoCaptureMinImportance` (1–5)
- Переключатель `TradingExperienceExportEnabled`
- Поле `TradingExperiencePnlThreshold`

---

## Порядок реализации

Реализовывать в следующем порядке (каждый шаг компилируется независимо):

1. **`AgentRole` enum + `RoleManager`** — базовая структура, без зависимостей.
2. **`RoleSkillIndex`** — обновить `GeneratedSkillTaskMatcher` для роли.
3. **`RoleAwareMemoryRouter`** — интегрировать в `ExternalBrainService.BuildContextAsync`.
4. **`RoleSession` + `RoleContextBlockService`** — добавить блок в `BuildOutboundHermesPrompt`.
5. **`RoleExperienceCapture`** — автосохранение черновиков.
6. **`TradingExperienceExporter`** — post-trade episodic memory.
7. **UI** — Role Switcher и Settings.
8. **Vault structure init** — `VaultInitializer`.

---

## Ключевые требования

- **Не ломать существующий код.** Все новые сервисы — аддитивны. `TradingModeEnabled` в `HermesSettings` продолжает работать, просто делегируется в `RoleManager.SwitchRole("Trader")`.
- **Производительность.** `FilterAndBoost` работает по снимку кэша (`ImmutableList`). `RoleSkillIndex.Rebuild` — при каждом reload каталога. Нет disk I/O в hot path.
- **Graceful degradation.** Если vault не настроен — `RoleAwareMemoryRouter` возвращает пустой список без исключений. `TradingExperienceExporter` — no-op если bridge недоступен.
- **Логи.** Все новые сервисы используют префиксы: `[role-manager]`, `[role-memory]`, `[role-skill]`, `[role-capture]`, `[trading-experience]`.
- **Тесты.** Для `RoleAwareMemoryRouter.FilterAndBoost` написать unit-тесты: проверить boost для правильной роли, penalty для чужой, fallback для Universal.

---

## Ожидаемый результат

После реализации:

1. Пользователь пишет «трейдинг» → роль переключается, промпт получает торговые воспоминания + навыки + контекст сессии, все новые знания автосохраняются с тегом `trading`.
2. Крупная сделка или emergency stop → эпизодическая запись появляется в `Knowledge/Trading/Episodes/` и подмешивается в следующий запрос в режиме Trader.
3. Пользователь пишет «разработчик» → индекс навыков переключается на `Developer`-корпус, vault фильтрует по `dotnet/code` тегам.
4. Skill resolver работает быстрее и точнее, потому что ищет только по навыкам активной роли.
