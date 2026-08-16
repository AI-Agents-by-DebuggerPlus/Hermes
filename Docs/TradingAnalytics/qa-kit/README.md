# QA — проверки Trading Analytics

| Файл | Назначение |
|------|------------|
| `run_all_checks.ps1` | Автотесты (pytest, IPC smoke) |
| `MANUAL_CHECKLIST.md` | Что смотреть глазами после запуска приложений |
| `last_report.txt` | Отчёт автотестов |

**Запуск приложений для визуальной оценки:** не через чат агента — используй **Hermes.Wpf Launcher** → раздел **Testing**.

## Для агента Hermes

### «Проверь экосистему» / автотесты
1. Запусти `qa/run_all_checks.ps1`.
2. Кратко PASS/FAIL.
3. Не путай с `~/.hermes/skills/`.
4. Если нужна визуальная оценка UI — скажи открыть Hermes.Wpf Launcher → Testing.
