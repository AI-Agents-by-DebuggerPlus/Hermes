# Показания воды — Рені (my.renivodokanal.od.ua)

## Без участия каждый месяц

**Логинить вручную каждый раз не нужно.** Один раз настраиваете — дальше 1-го числа планировщик (или Hermes.Wpf) передаёт показания сами.

### Вариант A — сохранённая сессия (рекомендуется)

1. `.\run_submit.ps1 -login` — войти в **окне Chromium Playwright** (не в обычном Chrome).
2. На сайте отметить **«Запам'ятати мене»**, дойти до страницы показаний.
3. Enter в PowerShell → в логе должно быть **`SESSION_OK`**.
4. `.\run_submit.ps1 -CheckSession` — проверка без передачи.
5. `.\register_scheduled_tasks.ps1` (от администратора) — автозапуск 1-го числа в 09:00.

Cookies хранятся в `d:\Documents\Utilities\water\browser-profile\`.

### Вариант B — логин/пароль в локальном файле (полный автомат)

Если сессия сбросится, скрипт войдёт сам:

1. Скопировать `reni_water.env.example` → `reni_water.env` (уже в `.gitignore`).
2. Раскомментировать и заполнить:
   ```
   RENI_LOGIN_USER=...
   RENI_LOGIN_PASSWORD=...
   ```
3. `.\run_submit.ps1 -CheckSession` → **`SESSION_OK`**.

Пароль **не** попадает в git.

## Hermes.Wpf

Вкладка **Навыки**: вход, проверка сессии, передача, подтверждение. В чате: **«Передай показания»**, **«принял»**.

## Команды

```powershell
cd D:\Programming\AI_Agents\Hermes\scripts\reni_water
.\run_submit.ps1 -login
.\run_submit.ps1 -CheckSession
.\run_submit.ps1
.\run_submit.ps1 -Ack
.\register_scheduled_tasks.ps1
```

Скриншоты: `d:\Documents\Utilities\water\HermesScreenShots\`

## Язык на сайте

Интерфейс — **украинский** (Показник на початок місяця → Новий показник → Передати). Playwright без Google Translate.
