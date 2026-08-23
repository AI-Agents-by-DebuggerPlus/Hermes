# BioStack — чат, ссылки и AVG

**Проверено:** 2026-08-22 (New CLI Session) — агент показал URL в чате и открыл его в **AVG Secure Browser**.

## Два канала открытия ссылок

| Канал | Как | Браузер |
|--------|-----|---------|
| Клик по ссылке в Hermes.Wpf | `ChatMessageLinkifier` → `Process.Start(url, UseShellExecute=true)` | Браузер по умолчанию Windows (у тебя AVG) |
| «Открой ссылку» агенту | skill **`open-url-avg`** → `open.sh` → `AVGBrowser.exe` | AVG Secure Browser явно |

## Skill `open-url-avg`

- В репозитории (зеркало): [`skills/open-url-avg/`](skills/open-url-avg/)
- На машине агента: `~/.hermes/skills/domain/open-url-avg/`
- В проекте BioStack: `HermesProjects/BioStack/hermes/skills/open-url-avg/` + правила в `AGENTS.md`

```bash
bash ~/.hermes/skills/domain/open-url-avg/open.sh 'https://…'
```

Бинарник: `C:\Program Files\AVG\Browser\Application\AVGBrowser.exe`  
Успех в terminal: `ok avg`

### Запрещено для «открой ссылку»

- `browser_navigate` / tool `browser` (Playwright) — на iHerb Cloudflare «Just a moment…» + timeout 60s
- Жёсткий `chrome.exe` / `msedge.exe`, если цель — тот же браузер, что у пользователя (AVG)

### После смены правил

Если агент снова зовёт Playwright — **New CLI Session** (кнопка в чате): старая сессия залипает на `browser_navigate`.

## Кликабельные ссылки в чате

Код: `Hermes.Wpf/Services/ChatMessageLinkifier.cs`, привязка в `ChatView.xaml`.

Поддерживаются:

- голые `https://…` / `http://…` / `file://…`
- markdown `[текст](https://…)`

## Связанные файлы

- `Docs/BioStack/BioStack_Profile_Summary_2026-07-13.md` — профиль / стек добавок
- `Docs/Plans/TASKS_CLI_Skills_Orchestration.md` — оркестрация skills CLI
- `Docs/TradingAnalytics/skills/open-local-artifact/` — открытие локальных HTML (не URL магазинов)
