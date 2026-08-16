# Trading Analytics — kit экосистемы

Шаблоны копируются в папку проекта агента `HermesProjects/Trading Analytics/`:

| Kit | Куда |
|-----|------|
| `ecosystem-kit/` | `hermes/ecosystem/` |
| `qa-kit/` | `qa/` (автотесты + ручной чеклист) |

Назначение: **on-demand** чтение агентом (INDEX → один howto / QA), без раздувания `~/.hermes/` и без инъекции в каждый turn WPF.

Установка: автоматически при `EnsureProjectHermesArtifacts` для проекта с именем *Trading Analytics*, либо вручную скопировать kit → соответствующие папки.

QA: агент запускает `qa/run_all_checks.ps1`, затем даёт релевантные пункты из `qa/MANUAL_CHECKLIST.md`.
