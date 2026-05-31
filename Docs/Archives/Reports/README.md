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
