# HERMES WPF UI

## Command Center + Connection Module (Cursor Implementation)

**C# · WPF · MVVM · WSL · .NET 8**

---

# 📌 Описание

Полноценный desktop-интерфейс для AI-агента **Hermes**, заменяющий Telegram UI и добавляющий:

* управление проектами
* чат с агентом
* live терминал
* автоматическую настройку окружения (WSL + Hermes)
* статус подключения и автопереподключение

---

# 🚀 Основные возможности

## 💬 Chat System

* Отправка команд Hermes
* Ответы в реальном времени
* История по проектам

## 📁 Project Manager

* Список проектов
* Быстрый запуск Hermes в нужной директории

## 🖥 Terminal Output

* stdout / stderr в реальном времени
* авто-скролл

## ⚡ Quick Actions

* `gateway run`
* `status`
* `reset webhook`
* `analyze code`

## 🔌 Connection Module (v2)

* Авто-проверка окружения (WSL + Hermes)
* Установка из UI
* Status Indicator (🟢/🟡/🔴)
* Auto-reconnect watchdog
* Setup Wizard

---

# 🏗 Архитектура

### MVVM

```
UI (Views)
   ↓
ViewModels (Logic + Binding)
   ↓
Services (WSL / Files / JSON)
```

---

# 📁 Структура проекта

```bash
HermesWPF/

├── Models/
│   ├── ChatMessage.cs
│   ├── HermesProject.cs
│   ├── ConnectionStatus.cs
│   └── HermesSettings.cs
│
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── SetupWizardViewModel.cs
│   └── SettingsViewModel.cs
│
├── Views/
│   ├── MainWindow.xaml
│   ├── StatusIndicator.xaml
│   ├── SetupWizardWindow.xaml
│   └── SettingsWindow.xaml
│
├── Services/
│   ├── HermesService.cs
│   ├── ConnectionService.cs
│   ├── ProjectService.cs
│   ├── HistoryService.cs
│   └── SettingsService.cs
│
├── Commands/
│   └── RelayCommand.cs
│
├── Converters/
│   └── ConnectionStateToColorConverter.cs
│
└── Resources/
    └── Styles.xaml
```

---

# 🔄 Поток данных

```
User Input
   ↓
MainViewModel.SendCommand
   ↓
HermesService (WSL)
   ↓
Output → Terminal
   ↓
Result → Messages
   ↓
History Save
```

---

# 🔌 Connection System

## 🧪 Preflight Checks

1. WSL доступен
2. venv существует
3. Hermes установлен
4. Hermes status работает

---

## 🔁 Состояния подключения

* 🔴 Disconnected
* 🟡 Checking / Connecting
* 🟢 Connected
* ❌ Error

---

## ⚙️ Settings (сохраняются в `%APPDATA%`)

```json
{
  "VenvPath": "~/hermes-agent/venv",
  "HermesCommand": "hermes",
  "ChatTimeoutSeconds": 60,
  "AutoReconnect": true
}
```

---

# 🧠 Setup Wizard (первый запуск)

### Step 1 — Проверка

* Автоматический preflight

### Step 2 — Установка

* WSL (если отсутствует)
* Hermes (pip install)

### Step 3 — Готово

* Подключение установлено

---

# 🟢 Status Indicator

UI-компонент:

* ● Красный — нет соединения
* ● Жёлтый — подключение
* ● Зелёный — подключено

- кнопка reconnect

---

# ⚡ Quick Actions

| Кнопка    | Команда                  |
| --------- | ------------------------ |
| ▶ Gateway | `gateway run`            |
| ◉ Status  | `status`                 |
| ↺ Reset   | `gateway reset-webhook`  |
| ⬡ Analyze | `chat -z "Analyze code"` |

---

# 🧩 Ключевые компоненты

## HermesService

* WSL bridge
* запуск hermes

## ConnectionService

* preflight
* install
* reconnect watchdog

## SettingsService

* JSON настройки

---

# 🔧 Реализация (по шагам)

## Phase 1 — Scaffold

* структура проекта
* стили

## Phase 2 — Services

* HermesService
* ConnectionService

## Phase 3 — ViewModels

* Main + Setup + Settings

## Phase 4 — UI

* 3 колонки
* чат / проекты / терминал

## Phase 5 — Polish

* анимации
* обработка ошибок
* авто-скролл

---

# ⚠️ Ограничения

* ❗ Все WSL-вызовы — async
* ❗ UI поток не блокируется
* ❗ авто-сохранение настроек
* ❗ потоковый вывод терминала
* ❗ обработка ошибок

---

# 🧪 Connection State Diagram

```
App Start
   ↓
First Run? → Setup Wizard
   ↓
Preflight
   ↓
Connected / Error
   ↓
Watchdog (auto reconnect)
```

---

# 🎯 Цель проекта

Создать **полностью автономный GUI для Hermes**, где:

> пользователь никогда не открывает терминал

---

# 🧠 Cursor Prompt

Используй оба промпта:

* UI Prompt (v1)
* Connection Prompt (v2)

---

# 📦 Готово

Проект после реализации:

* выглядит как полноценный IDE-like инструмент
* автоматически настраивает окружение
* стабильно работает с Hermes

---
