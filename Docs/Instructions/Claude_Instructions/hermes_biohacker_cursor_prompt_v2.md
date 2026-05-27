# Cursor Prompt v2: Biohacker Role + TradingPlatform Audit

## Важно: прочитай перед началом работы

Этот промпт написан с учётом актуального состояния кодовой базы на 2026-05-25.
Ряд подсистем **уже реализован** — не переписывай их. Работа делится на три категории:

| Категория | Что делать |
|-----------|------------|
| 🔴 **Создать с нуля** | Новые файлы, которых нет в проекте |
| 🟡 **Расширить** | Добавить строки/ветки в существующий код без изменения логики |
| ✅ **Не трогать** | Уже реализовано и работает — только читай для контекста |

---

## Контекст проекта

**Репозиторий:** `D:\Programming\AI_Agents\Hermes`
**Основной клиент:** `Hermes.Wpf` — WPF-приложение, фронтенд для агента Hermes (WSL `hermes chat`).
**Торговый терминал:** `Hermes.TradingPlatform` — отдельный WPF-процесс, общается с Hermes.Wpf через file-bridge.
**Файл настроек:** `%AppData%\HermesWpf\settings.json` (класс `HermesSettings`).

---

## ✅ Уже реализовано — не трогать

Следующие сервисы **полностью реализованы** и работают. Читай их только для понимания архитектуры и точек расширения:

```
Hermes.Wpf/Services/ExternalBrainService.cs          // vault, FileSystemWatcher, BuildContextAsync
Hermes.Wpf/Services/MemoryVectorIndex.cs             // TF-IDF + Ollama embeddings
Hermes.Wpf/Services/MemoryExtractorService.cs        // черновик опыта после каждого ответа
Hermes.Wpf/Services/RoleExperienceCapture.cs         // авто-захват в Knowledge/<role>/
Hermes.Wpf/Services/RoleManager.cs                   // переключение ролей, алиасы, persist
Hermes.Wpf/Services/RoleAwareMemoryRouter.cs         // бусты/штрафы по ролям
Hermes.Wpf/Services/RoleSkillIndex.cs               // usage stats навыков по ролям
Hermes.Wpf/Services/GeneratedSkillCatalogService.cs  // каталог навыков
Hermes.Wpf/Services/GeneratedSkillTaskMatcher.cs     // resolver: Rank(userTask, role, ...)
Hermes.Wpf/Services/SkillGenerationService.cs        // кристаллизация skill_save
Hermes.Wpf/Services/WslAgentMemorySyncService.cs     // USER.md/MEMORY.md -> vault
Hermes.Wpf/Services/VaultInitializer.cs              // создание папочной структуры vault
Hermes.Wpf/Services/TradingPlatformBridgeService.cs  // bridge с торговым терминалом
Hermes.Wpf/ViewModels/MainViewModel.cs               // оркестрация хода чата
```

Текущие роли в `AgentRole` enum:
```csharp
Universal, Developer, Trader, EnglishTutor, PersonalManager
```

Текущая структура vault:
```
{vault}/
├── Identity/
├── Knowledge/
│   ├── Trading/
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

---

## Часть 1: Аудит Hermes.TradingPlatform

### Задача 1.1 — Прочитай и зафикcируй актуальное состояние

Перед написанием любого кода **прочитай следующие файлы** в `Hermes.TradingPlatform`:

```
Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs
Hermes.TradingPlatform.Data/Persistence/TradeJournalFileWriter.cs
Hermes.TradingPlatform.Data/Persistence/TradingStatePersistence.cs
Hermes.TradingPlatform.Data/Persistence/TradingSessionStateFileStore.cs
Hermes.TradingPlatform.Core/Domain/RiskProfile.cs
Hermes.TradingPlatform.Shared/Bridge/TradingBridgePaths.cs
Hermes.Wpf/Services/TradingPlatformBridgeService.cs
```

По каждому файлу зафиксируй в коде как комментарий (`// AUDIT 2026-05-25:`):
- Какие события генерирует (OrderFilled, RiskRejected, EmergencyStop, StrategyEnabled и т.д.)
- Что из этого **попадает** в bridge snapshot (и, следовательно, видно Hermes.Wpf)
- Что **не попадает** в bridge и теряется для External Brain

Также проверь: изменился ли формат `snapshot.json` по сравнению с описанием в
`Docs/Report/Hermes_Trading_Platform_Integration.md`. Если изменился — зафикcируй diff.

### Задача 1.2 — Проверь наличие TradingExperienceExporter

Проверь, существует ли файл:
```
Hermes.Wpf/Services/TradingExperienceExporter.cs
```

**Если файл существует** — прочитай его и пропусти Задачу 1.3.
**Если не существует** — выполни Задачу 1.3.

### Задача 1.3 🔴 — Создать TradingExperienceExporter (если не существует)

**Файл:** `Hermes.Wpf/Services/TradingExperienceExporter.cs`

Сервис подписывается на diff snapshot и при значимых событиях создаёт
эпизодическую запись в vault `Knowledge/Trading/Episodes/`.

```csharp
public class TradingExperienceExporter : IDisposable
{
    private readonly ExternalBrainService _brain;
    private readonly LogService _log;
    private readonly HermesSettings _settings;
    private TradingSnapshot? _previousSnapshot;

    // Вызывается из TradingPlatformBridgeService при каждом обновлении snapshot.
    // TradingSnapshot — используй тот тип, который уже есть в TradingPlatformBridgeService.
    public async Task OnSnapshotUpdatedAsync(object currentSnapshot);

    private TradeSignificantEvent? DetectEvent(object prev, object current);
    private async Task WriteEpisodeAsync(TradeSignificantEvent evt, object snapshot);
}

public record TradeSignificantEvent(
    TradeEventKind Kind,
    string? Symbol,
    decimal? RealizedPnl,
    string? RejectionReason,
    string? StrategyId);

public enum TradeEventKind
{
    LargeRealizedPnl,     // |PnL за ход| > TradingExperiencePnlThreshold
    RiskRejection,        // появился новый rejection в snapshot
    EmergencyStop,        // equity упала > TradingExperienceDrawdownThreshold от пика
    StrategyChanged       // включена/выключена стратегия
}
```

**Шаблон эпизода** (путь: `Knowledge/Trading/Episodes/{yyyy-MM-dd_HH-mm}_{kind}.md`):

```markdown
---
type: episodic
role: Trader
tags: [trading, episode, {event_kind_lower}]
importance: 4
captured: auto
date: {ISO datetime}
---

# {EventKind}: {Symbol}

**Событие:** {описание}
**Баланс:** {balance} | **Equity:** {equity} | **Открытых позиций:** {count}
**Результат:** {realized_pnl или rejection_reason}
**Активные стратегии:** {список}

## Урок
<!-- Hermes заполнит при следующем обращении в режиме Trader -->
```

После записи:
```csharp
_brain.RestartWatcherAndReload("trading-experience");
_log.Info($"[trading-experience] Captured {evt.Kind} episode");
```

**Настройки** — добавить в `HermesSettings`:
```csharp
public bool TradingExperienceExportEnabled { get; set; } = true;
public decimal TradingExperiencePnlThreshold { get; set; } = 50m;
public double TradingExperienceDrawdownThreshold { get; set; } = 0.05;
```

**Интеграция в TradingPlatformBridgeService:**
Найди метод, читающий `snapshot.json` (вероятно `ReadSnapshotAsync` или аналог).
После успешного чтения добавь:

```csharp
if (_settings.TradingExperienceExportEnabled && _tradingExperienceExporter != null)
    await _tradingExperienceExporter.OnSnapshotUpdatedAsync(snapshot);
```

### Задача 1.4 🟡 — Добавить `Knowledge/Trading/Episodes/` в VaultInitializer

В `VaultInitializer.cs` найди метод, создающий папки vault, добавь:
```csharp
Path.Combine(vaultRoot, "Knowledge", "Trading", "Episodes"),
```

---

## Часть 2: Роль Biohacker

### Задача 2.1 🟡 — Расширить AgentRole enum

**Файл:** где объявлен `AgentRole` (найди через поиск по проекту).

Добавить одну строку:
```csharp
public enum AgentRole
{
    Universal,
    Developer,
    Trader,
    EnglishTutor,
    PersonalManager,
    Biohacker        // <- добавить
}
```

### Задача 2.2 🟡 — Расширить RoleAwareMemoryRouter

**Файл:** `Hermes.Wpf/Services/RoleAwareMemoryRouter.cs`

Найди словари `RolePrimaryTags` и `RoleVaultPaths`. Добавь:

```csharp
// В RolePrimaryTags:
[AgentRole.Biohacker] = new[]
{
    "health", "supplement", "nootropic", "sleep", "nutrition", "exercise",
    "cognitive", "energy", "mood", "recovery", "биохакинг", "бад", "ноотроп",
    "сон", "питание", "тренировка", "самочувствие", "продуктивность", "здоровье"
},

// В RoleVaultPaths:
[AgentRole.Biohacker] = new[]
{
    "Health/Supplements", "Health/Protocols", "Health/Journal",
    "Health/Schedule", "Health/Goals", "Health/Metrics", "Identity"
},
```

Если в коде есть массив тегов для penalty других ролей — добавь для Biohacker
штраф на `trading`, `dotnet`, `csharp`, `english`, `vocabulary`.

### Задача 2.3 🟡 — Расширить RoleExperienceCapture

**Файл:** `Hermes.Wpf/Services/RoleExperienceCapture.cs`

Найди switch/dictionary, который маппит роль → (папка vault, тег). Добавь:
```csharp
AgentRole.Biohacker => ("Health/Journal", "health"),
```

### Задача 2.4 🟡 — Расширить RoleManager: алиасы

**Файл:** `Hermes.Wpf/Services/RoleManager.cs`

Найди словарь алиасов. Добавь:
```csharp
["biohacker"]    = AgentRole.Biohacker,
["biohacking"]   = AgentRole.Biohacker,
["биохакер"]     = AgentRole.Biohacker,
["биохакинг"]    = AgentRole.Biohacker,
["здоровье"]     = AgentRole.Biohacker,
["бады"]         = AgentRole.Biohacker,
["ноотропы"]     = AgentRole.Biohacker,
["самочувствие"] = AgentRole.Biohacker,
```

### Задача 2.5 🟡 — Расширить VaultInitializer

**Файл:** `Hermes.Wpf/Services/VaultInitializer.cs`

Добавь папки:
```csharp
Path.Combine(vaultRoot, "Health"),
Path.Combine(vaultRoot, "Health", "Supplements"),
Path.Combine(vaultRoot, "Health", "Protocols"),
Path.Combine(vaultRoot, "Health", "Journal"),
Path.Combine(vaultRoot, "Health", "Schedule"),
Path.Combine(vaultRoot, "Health", "Goals"),
Path.Combine(vaultRoot, "Health", "Metrics"),
```

После создания папок запиши стартовые файлы (только если не существуют):

**`Health/Supplements/README.md`:**
```markdown
---
type: reference
role: Biohacker
tags: [supplement, reference]
importance: 3
---

# Карточки БАДов и ноотропов

Каждый файл в этой папке — карточка одного препарата.
Hermes создаёт и обновляет карточки автоматически через {"bio":"update_supplement",...}.

Поля: name, category, status, dose_mg, timing, frequency,
stock_units, stock_days_left, reorder_threshold, observed_effects, stack_compatibility.
```

**`Health/Schedule/README.md`:**
```markdown
---
type: reference
role: Biohacker
tags: [schedule, reference]
importance: 3
---

# Распорядок дня

- workday.md — рабочий день
- weekend.md — выходной день
- optimized_*.md — варианты под конкретные цели

Hermes обновляет расписание через {"bio":"update_schedule",...}.
```

**`Health/Goals/cognitive_peak.md`** (только если файл не существует):
```markdown
---
type: health_goal
role: Biohacker
tags: [goal, health, cognitive, biohacking]
goal_id: cognitive_peak
title: Стабильная ясность ума и физическая энергия
priority: 1
status: active
importance: 5
---

# Цель: стабильная когнитивная ясность и энергия

## Метрики успеха
- Фокус 8+/10 не менее 5 дней в неделю
- Энергия при подъёме 7+/10 стабильно
- Сон 7–8 ч с субъективным качеством 7+/10

## Активные вмешательства
<!-- Hermes заполнит после первого разговора о здоровье -->

## Текущий статус
<!-- Обновляется на основе Health/Journal/ -->
```

### Задача 2.6 🔴 — Создать модели данных Biohacker

**Папка:** `Hermes.Wpf/Models/Biohacker/`

Создай файлы: `SupplementCard.cs`, `DailyHealthLog.cs`, `DailySchedule.cs`,
`HealthGoal.cs`, `HealthMetricsSummary.cs`.

Каждая модель должна:
- сериализоваться в Markdown с YAML frontmatter совместимым с `ExternalBrainMarkdown.cs`
- иметь методы `ToMarkdown()` и `static FromMemoryItem(MemoryItem item)`

```csharp
// SupplementCard.cs
public class SupplementCard
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";     // mineral|vitamin|nootropic|adaptogen|amino|other
    public string Status { get; set; } = "active"; // active|paused|finished|out_of_stock
    public int DoseMg { get; set; }
    public string DoseUnit { get; set; } = "mg";
    public string Timing { get; set; } = "";        // morning|afternoon|evening|before_sleep|with_meal|fasted
    public string Frequency { get; set; } = "daily";
    public int StockUnits { get; set; }
    public int StockDaysLeft { get; set; }
    public int ReorderThreshold { get; set; } = 14;
    public List<string> ObservedEffects { get; set; } = new();
    public string StackCompatibility { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime LastUpdated { get; set; }
    public string SourceFile { get; set; } = "";

    public string ToMarkdown();
    public static SupplementCard? FromMemoryItem(MemoryItem item);
}

// DailyHealthLog.cs
public class DailyHealthLog
{
    public DateTime Date { get; set; }
    public int? WakeTimeMinutes { get; set; }     // минуты от полуночи (06:30 = 390)
    public int? SleepQuality { get; set; }        // 1–10
    public int? EnergyMorning { get; set; }       // 1–10
    public int? Mood { get; set; }                // 1–10
    public int? FocusDay { get; set; }            // 1–10
    public int? Productivity { get; set; }        // 1–10
    public int? Stress { get; set; }              // 1–10
    public int? PhysicalWellbeing { get; set; }   // 1–10
    public List<SupplementTaken> SupplementsTaken { get; set; } = new();
    public string PhysicalActivity { get; set; } = "";
    public string Nutrition { get; set; } = "";
    public string Notes { get; set; } = "";
    public string SourceFile { get; set; } = "";

    public string ToMarkdown();
    public static DailyHealthLog? FromMemoryItem(MemoryItem item);
}

public record SupplementTaken(string Name, int DoseMg, string Timing, bool Taken);

// DailySchedule.cs
public class DailySchedule
{
    public string ScheduleType { get; set; } = "workday"; // workday|weekend|optimized
    public string Goal { get; set; } = "";
    public string Status { get; set; } = "active";
    public List<ScheduleBlock> Blocks { get; set; } = new();
    public List<string> Rules { get; set; } = new();
    public string Issues { get; set; } = "";
    public DateTime LastUpdated { get; set; }
    public string SourceFile { get; set; } = "";

    public string ToMarkdown();
    public static DailySchedule? FromMemoryItem(MemoryItem item);
}

public record ScheduleBlock(string Time, string Activity, string Category, string Supplement);

// HealthGoal.cs
public class HealthGoal
{
    public string GoalId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Priority { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? TargetDate { get; set; }
    public List<string> SuccessMetrics { get; set; } = new();
    public List<string> ActiveInterventions { get; set; } = new();
    public string SourceFile { get; set; } = "";

    public string ToMarkdown();
    public static HealthGoal? FromMemoryItem(MemoryItem item);
}

// HealthMetricsSummary.cs
public record HealthMetricsSummary(
    double AvgSleepQuality,
    double AvgEnergyMorning,
    double AvgFocus,
    double AvgMood,
    double AvgProductivity,
    double AvgStress,
    int DaysAnalyzed,
    string Trend);  // improving|stable|declining
```

### Задача 2.7 🔴 — Создать BiohackerStateService

**Файл:** `Hermes.Wpf/Services/BiohackerStateService.cs`

```csharp
public class BiohackerStateService
{
    private readonly ExternalBrainService _brain;
    private readonly HermesSettings _settings;
    private readonly LogService _log;

    public BiohackerStateService(ExternalBrainService brain, HermesSettings settings, LogService log);

    // Supplements
    public Task<IReadOnlyList<SupplementCard>> GetAllSupplementsAsync();
    public Task<IReadOnlyList<SupplementCard>> GetActiveStackAsync();  // status=active, сортировка по Timing
    public Task SaveSupplementCardAsync(SupplementCard card);
    public Task UpdateStockAsync(string supplementName, int dosesUsed = 1);
    public Task<IReadOnlyList<SupplementCard>> GetLowStockAlertsAsync();

    // Daily Log
    public Task<DailyHealthLog> GetOrCreateTodayLogAsync();
    public Task SaveDailyLogAsync(DailyHealthLog log);
    public Task<IReadOnlyList<DailyHealthLog>> GetRecentLogsAsync(int days = 14);

    // Schedule
    public Task<DailySchedule?> GetActiveScheduleAsync(DayOfWeek day); // workday/weekend по дню
    public Task SaveScheduleAsync(DailySchedule schedule);

    // Goals
    public Task<IReadOnlyList<HealthGoal>> GetActiveGoalsAsync();

    // Analytics
    public Task<HealthMetricsSummary> ComputeMetricsSummaryAsync(int days = 7);

    // Строит краткий блок для промпта (работает по кэшу ExternalBrainService, без disk I/O)
    public Task<string> BuildContextSnapshotAsync();
}
```

**Реализация `BuildContextSnapshotAsync`** — формат блока:

```
[Активный стек — сегодня]
• Альфа-GPC 300 мг — утром
• Магний глицинат 400 мг — перед сном
⚠ Омега-3: осталось 12 дней — пора заказать

[Метрики (7 дней avg)]
Сон: 7.2 | Энергия: 6.8 | Фокус: 7.4 | Тренд: stable

[Активные цели]
1. Стабильная ясность ума (приоритет 1)

[Распорядок — Рабочий день]
Deep work: 08:15–09:45 | Последний кофе: до 14:00
```

**Реализация чтения из vault:**
Используй `ExternalBrainService` (уже существует) для получения `MemoryItem` по тегам/путям.
Не создавай отдельный FileSystemWatcher — парсируй из кэша ExternalBrainService:

```csharp
var allItems = await _brain.GetAllMemoriesAsync();
var supplements = allItems
    .Where(m => m.Tags.Contains("supplement") && m.SourceFile.Contains("Health/Supplements"))
    .Select(SupplementCard.FromMemoryItem)
    .Where(c => c != null)
    .ToList();
```

### Задача 2.8 🔴 — Создать SupplementStockTracker

**Файл:** `Hermes.Wpf/Services/SupplementStockTracker.cs`

```csharp
public class SupplementStockTracker
{
    private readonly BiohackerStateService _state;
    private readonly LogService _log;
    private DateTime _lastCheckDate = DateTime.MinValue;

    // Выполняется не чаще одного раза в день
    public async Task RunDailyCheckIfNeededAsync();

    // Списывает по 1 дозе для всех active+daily БАДов
    private async Task DeductDailyDosesAsync();

    // Предупреждения для контекстного блока
    public async Task<IReadOnlyList<StockAlert>> GetAlertsAsync();
}

public record StockAlert(string SupplementName, int DaysLeft, int ReorderThreshold)
{
    public bool IsCritical => DaysLeft <= ReorderThreshold / 2;
}
```

### Задача 2.9 🔴 — Создать BiohackerIntentParser

**Файл:** `Hermes.Wpf/Services/BiohackerIntentParser.cs`

Парсит все `{"bio":"..."}` блоки из текста ответа Hermes.

```csharp
public class BiohackerIntentParser
{
    // Возвращает: список намерений + текст ответа без JSON-блоков
    public (IReadOnlyList<BiohackerIntent> Intents, string CleanText) TryParseAll(string rawResponse);
}

public abstract record BiohackerIntent;
public record LogSupplementIntent(string Name, int DoseMg, string Timing, DateTime Date) : BiohackerIntent;
public record UpdateSupplementIntent(SupplementCard Card) : BiohackerIntent;
public record UpdateStockIntent(string Name, int DosesUsed) : BiohackerIntent;
public record LogMetricsIntent(DateTime Date, int? SleepQuality, int? EnergyMorning,
    int? FocusDay, int? Mood, int? Productivity, int? Stress, string Notes) : BiohackerIntent;
public record UpdateScheduleIntent(DailySchedule Schedule) : BiohackerIntent;
public record SetGoalIntent(HealthGoal Goal) : BiohackerIntent;
public record OptimizeScheduleIntent(string ScheduleType, string Reason,
    IReadOnlyList<ScheduleChange> Changes) : BiohackerIntent;
public record ScheduleChange(string TimeFrom, string TimeTo, string Block);
```

**Форматы JSON из ответа Hermes:**

```json
{"bio":"log_supplement","name":"Альфа-GPC","dose_mg":300,"timing":"morning","date":"2026-05-25"}

{"bio":"update_supplement","name":"Магний глицинат","dose_mg":400,"timing":"before_sleep",
 "status":"active","stock_units":60,"reorder_threshold":14,
 "observed_effects":["улучшение сна","снижение тревожности"],
 "stack_compatibility":"совместим с L-теанин; кальций — разносить на 2ч"}

{"bio":"update_stock","name":"Альфа-GPC","doses_used":1}

{"bio":"log_metrics","date":"2026-05-25",
 "sleep_quality":7,"energy_morning":6,"focus_day":8,
 "mood":7,"productivity":8,"stress":3,"notes":"хорошая тренировка утром"}

{"bio":"update_schedule","schedule_type":"workday",
 "blocks":[{"time":"06:30","activity":"Подъём","category":"wake","supplement":""},
            {"time":"08:15","activity":"Deep work","category":"cognitive","supplement":"Альфа-GPC"}],
 "rules":["Кофеин до 14:00","Экраны выключить за 1ч до сна"]}

{"bio":"optimize_schedule","schedule_type":"workday",
 "reason":"проект требует максимального deep work",
 "changes":[{"time_from":"08:15","time_to":"07:00","block":"Deep work блок 1"}]}

{"bio":"set_goal","goal_id":"energy_baseline","title":"Стабильная энергия 7+/10",
 "priority":2,"success_metrics":["Энергия утром 7+/10","Без кофе до 10:00"]}
```

### Задача 2.10 🔴 — Создать BiohackerIntentHandler

**Файл:** `Hermes.Wpf/Services/BiohackerIntentHandler.cs`

```csharp
public class BiohackerIntentHandler
{
    private readonly BiohackerStateService _state;
    private readonly SupplementStockTracker _stockTracker;
    private readonly ExternalBrainService _brain;
    private readonly LogService _log;

    public async Task HandleAsync(BiohackerIntent intent);

    private async Task HandleLogSupplement(LogSupplementIntent i);
    private async Task HandleUpdateSupplement(UpdateSupplementIntent i);
    private async Task HandleUpdateStock(UpdateStockIntent i);
    private async Task HandleLogMetrics(LogMetricsIntent i);
    private async Task HandleUpdateSchedule(UpdateScheduleIntent i);
    private async Task HandleOptimizeSchedule(OptimizeScheduleIntent i);
    private async Task HandleSetGoal(SetGoalIntent i);
}
```

После каждого `Handle*`:
```csharp
_brain.RestartWatcherAndReload("biohacker-intent");
_log.Info($"[biohacker-intent] Handled {intent.GetType().Name}");
```

### Задача 2.11 🔴 — Создать BiohackerPersonaInstructions

**Файл:** `Hermes.Wpf/Services/BiohackerPersonaInstructions.cs`

```csharp
public static class BiohackerPersonaInstructions
{
    public static string OutboundBlockRu => """
        --- РОЛЬ: БИОХАКЕР И МЕНЕДЖЕР ПРОДУКТИВНОСТИ ---
        Ты — персональный биохакер, нутрициолог и менеджер продуктивности пользователя.
        Ты знаешь всё о его здоровье, добавках, распорядке дня и целях.

        ГЛАВНЫЙ ПРИОРИТЕТ: стабильная ясность ума и физическая энергия как фундамент
        для продуктивной работы, трейдинга и личного развития.

        ОБЯЗАННОСТИ:
        - Запоминать ВСЁ о здоровье, БАДах, ноотропах, питании, физической активности
        - Фиксировать эффекты: что реально работает для этого конкретного пользователя
        - Отслеживать остатки БАДов и предупреждать заранее
        - Анализировать паттерны на основе дневника (что улучшает сон/фокус/энергию)
        - Рекомендовать изменения постепенно — одно вмешательство за раз
        - Оптимизировать распорядок дня под текущие задачи и долгосрочные цели

        СТИЛЬ:
        - Всегда опирайся на данные пользователя, не на общие рекомендации
        - Указывай, на основе каких наблюдений делаешь вывод
        - Если данных недостаточно — явно запрашивай их
        - Фокус на 1–2 приоритетных изменениях, не перегружай

        СТРУКТУРИРОВАННЫЕ ДАННЫЕ:
        Когда фиксируешь данные или обновляешь протокол — добавляй в конец ответа
        соответствующий {"bio":...} JSON-блок. Пользователь его не видит.
        Допускается несколько блоков подряд.

        Примеры триггеров:
        - Пользователь упомянул приём БАД → {"bio":"log_supplement",...}
        - Пользователь описал самочувствие → {"bio":"log_metrics",...}
        - Предлагаешь изменить расписание → {"bio":"optimize_schedule",...}
        - Обновляешь данные о добавке → {"bio":"update_supplement",...}
        ---
        """;
}
```

### Задача 2.12 🟡 — Интегрировать Biohacker в MainViewModel

**Файл:** `Hermes.Wpf/ViewModels/MainViewModel.cs`

#### 2.12.1 Поля и инициализация

```csharp
// Добавить поля рядом с другими сервисами:
private BiohackerStateService? _biohackerState;
private BiohackerIntentParser? _biohackerParser;
private BiohackerIntentHandler? _biohackerHandler;
private SupplementStockTracker? _supplementTracker;
```

Инициализировать в том же месте, где создаются другие сервисы (конструктор или Initialize):
```csharp
if (_settings.BiohackerEnabled)
{
    _biohackerState   = new BiohackerStateService(_externalBrain, _settings, _log);
    _biohackerParser  = new BiohackerIntentParser();
    _supplementTracker = new SupplementStockTracker(_biohackerState, _log);
    _biohackerHandler = new BiohackerIntentHandler(
        _biohackerState, _supplementTracker, _externalBrain, _log);
}
```

#### 2.12.2 В BuildOutboundHermesPrompt

После блока External Brain, перед Skill Resolver:
```csharp
if (_roleManager.CurrentRole == AgentRole.Biohacker && _biohackerState != null)
{
    outbound.AppendLine(BiohackerPersonaInstructions.OutboundBlockRu);
    var bioSnapshot = await _biohackerState.BuildContextSnapshotAsync();
    if (!string.IsNullOrWhiteSpace(bioSnapshot))
    {
        outbound.AppendLine("--- BIOHACKER CONTEXT ---");
        outbound.AppendLine(bioSnapshot);
        outbound.AppendLine("---");
    }
}
```

#### 2.12.3 В post-process после получения ответа Hermes

После разбора `skill_save` / `run_generated`, перед записью в чат:
```csharp
if (_roleManager.CurrentRole == AgentRole.Biohacker
    && _biohackerParser != null && _biohackerHandler != null)
{
    var (intents, cleanText) = _biohackerParser.TryParseAll(rawResponse);
    if (intents.Count > 0)
    {
        displayResponse = cleanText; // показывать без JSON-блоков
        foreach (var intent in intents)
            await _biohackerHandler.HandleAsync(intent);
        await _supplementTracker!.RunDailyCheckIfNeededAsync();
    }
}
```

#### 2.12.4 При активации роли Biohacker

В обработчике события `RoleChanged`:
```csharp
if (newRole == AgentRole.Biohacker && _supplementTracker != null)
    await _supplementTracker.RunDailyCheckIfNeededAsync();
```

### Задача 2.13 🟡 — UI: добавить цвет и Settings

**Конвертер цвета роли** (найди через поиск `ConnectionStateToColorConverter` или аналогичный):
```csharp
AgentRole.Biohacker => "#00BCD4",  // циан
```

**SettingsWindow** — добавить секцию «Биохакер»:
- `BiohackerEnabled` (bool, default: true)
- `BiohackerStockCheckOnStartup` (bool, default: true)
- `TradingExperienceExportEnabled` (bool, default: true)
- `TradingExperiencePnlThreshold` (decimal, default: 50)

**HermesSettings** — добавить поля:
```csharp
public bool BiohackerEnabled { get; set; } = true;
public bool BiohackerStockCheckOnStartup { get; set; } = true;
```

---

## Часть 3: Порядок выполнения

Строго в этом порядке — каждый шаг компилируется перед переходом к следующему:

1. **Аудит TradingPlatform** (Задача 1.1) — прочитай, зафикcируй в комментариях.
2. **AgentRole enum** (2.1) — одна строка.
3. **RoleAwareMemoryRouter** (2.2) — маппинги.
4. **RoleExperienceCapture** (2.3) — ветка switch.
5. **RoleManager алиасы** (2.4) — строки в словарь.
6. **VaultInitializer** (2.5) — папки + стартовые файлы.
7. **Модели данных** (2.6) — `SupplementCard`, `DailyHealthLog`, `DailySchedule`, `HealthGoal`, `HealthMetricsSummary`.
8. **BiohackerStateService** (2.7).
9. **SupplementStockTracker** (2.8).
10. **BiohackerIntentParser** (2.9) + unit-тесты (см. ниже).
11. **BiohackerIntentHandler** (2.10).
12. **BiohackerPersonaInstructions** (2.11).
13. **TradingExperienceExporter** (1.2–1.3) — проверить, создать если нет.
14. **VaultInitializer: Episodes** (1.4).
15. **MainViewModel интеграция** (2.12).
16. **UI** (2.13).

---

## Часть 4: Требования

- **Не ломать существующий код.** Только аддитивные изменения в существующих файлах.
- **Graceful degradation.** `BiohackerStateService` возвращает пустые коллекции если vault не настроен. Если `_biohackerState == null` — блок в промпте не добавляется, исключений нет.
- **Vault-совместимость.** Все `*.md` Biohacker используют YAML frontmatter совместимый с `ExternalBrainMarkdown.cs`. Поле `type` обязательно в каждом файле.
- **Без disk I/O в hot path.** `BuildContextSnapshotAsync` и `GetActiveStackAsync` работают по кэшу `ExternalBrainService.GetAllMemoriesAsync()`.
- **Логи.** Префиксы: `[biohacker]`, `[biohacker-stock]`, `[biohacker-intent]`, `[trading-experience]`.
- **Unit-тесты для BiohackerIntentParser:**
  - корректный одиночный JSON-блок
  - несколько блоков подряд в одном ответе
  - JSON с мусорным текстом вокруг
  - некорректный JSON (не должен бросать исключение)
  - пустая строка

---

## Ожидаемый результат

1. «биохакер» → роль переключается → промпт содержит персону + стек БАДов + метрики + предупреждения + цели.
2. Пользователь упомянул приём добавки → `{"bio":"log_supplement",...}` → карточка обновляется автоматически.
3. Пользователь описал самочувствие → `{"bio":"log_metrics",...}` → запись в `Health/Journal/`.
4. Через 14 дней дневника Hermes выявляет персональные паттерны и рекомендует с обоснованием на данных.
5. Крупная сделка / emergency stop → `TradingExperienceExporter` → запись в `Knowledge/Trading/Episodes/` → подмешивается в следующий Trader-запрос.
6. Все существующие роли работают без изменений.
