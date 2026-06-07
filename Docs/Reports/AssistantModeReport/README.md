# Hermes.Wpf — режим ассистента: опыт, навыки и кейс «водоканал»

**Дата отчёта:** 2026-05-28  
**Основной проект:** `Hermes.Wpf`  
**Кейс-стади:** передача показаний воды в Рeni vodokanal (`scripts/reni_water`) — по запросу и по расписанию  
**Связанные документы:** [Experience_And_Skills_Logic_Report](../../Archives/Report/Experience_And_Skills_Logic_Report.md), [TradingModeReport](../TradingModeReport/README.md), [scripts/reni_water/README.md](../../../scripts/reni_water/README.md)

---

## Назначение отчёта

Документ анализирует **как Hermes накапливает опыт** и **создаёт новые навыки** в процессе решения задач, и **насколько это применимо** к уже реализованному сценарию передачи показаний в водоканал.

Отчёт отвечает на вопросы:

1. Какие подсистемы отвечают за память и навыки?
2. Почему агент может «не помнить» настроенный водоканал?
3. Участвует ли Reni Water в learning pipeline?
4. Что нужно изменить, чтобы опыт и навыки работали с бытовыми автоматизациями?

---

## Кратко: два контура

| Контур | Что делает | Автоматизм |
|--------|------------|------------|
| **Опыт (memory)** | Markdown vault (External Brain), WSL sync, черновики из чата | Частичный: черновик после Hermes CLI; auto-capture по роли — с фильтрами |
| **Навыки (skills)** | `%AppData%\HermesWpf\skills\` + зеркало WSL | Создание — только по `skill_save` / «сохрани как навык»; подбор — TF-IDF matcher |

**Reni Water** — **третий контур**: жёстко зашитый локальный навык WPF. Он **не** проходит через ни опыт, ни generated skills.

---

## Структура отчёта

| Файл | Содержание |
|------|------------|
| [01_Overview.md](./01_Overview.md) | Архитектура, три типа «навыков», схема одного хода чата |
| [02_Experience_Accumulation.md](./02_Experience_Accumulation.md) | External Brain, MemoryExtractor, RoleCapture, WSL sync, история чата |
| [03_Skill_Generation.md](./03_Skill_Generation.md) | Кристаллизация, catalog, resolver, sandbox, исполнение |
| [04_Reni_Water_Case_Study.md](./04_Reni_Water_Case_Study.md) | Реализация водоканала, расписание, триггеры, разрывы с learning |
| [05_Gaps_And_Recommendations.md](./05_Gaps_And_Recommendations.md) | Пробелы, влияние trading/assistant mode, предложения |
| [06_Implementation.md](./06_Implementation.md) | Реализованный learning loop для Reni Water |

---

## TL;DR — кейс водоканала

```
«Передай показания»  →  TryHandleReniWaterLocalAsync  →  run_submit.ps1  →  Playwright
                         (без Hermes CLI, без ExtractExperience)

«Ты передавал показания?»  →  Hermes CLI  →  LLM не знает про Reni Water
                               (нет блока в промпте, vault пуст)

«Сохрани как навык»  →  skill_save JSON  →  новый навык в skills/
                         (не связан с встроенным Reni Water)
```

**Вывод:** передача показаний работает как **локальная автоматизация WPF**. Накопление опыта и создание навыков агентом **не подключены** к этому сценарию по умолчанию.
