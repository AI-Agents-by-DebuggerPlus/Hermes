Вот перевод содержимого документа в **Markdown (md)** — структура сохранена, пригодна для README или Cursor:

---

# HERMES WPF UI

## Command Center — Cursor Implementation Prompt

**C# · WPF · MVVM · WSL Bridge · .NET 8**

---

## 1. Назначение и цели проекта

Создать нативное WPF-приложение на C# (.NET 8), которое заменяет Telegram-интерфейс агента Hermes и добавляет полноценный менеджер проектов. Приложение должно взаимодействовать с локально установленным Hermes через WSL/консольный процесс.

### 🎯 Ключевые функции

* Чат с Hermes — отправка команд и получение ответов в реальном времени
* Project Manager — список папок/проектов, быстрый запуск Hermes в нужной директории
* Terminal Output — живой вывод stdout/stderr
* Control Panel — быстрые действия (gateway run, status, reset webhook)
* Session History — история диалогов по проектам

---

## 2. Архитектура проекта

Паттерн **MVVM**. Три слоя:

* UI (WPF Views)
* ViewModel (логика + биндинги)
* Services (WSL, файловая система, сериализация)

### 📁 Структура папок

```
HermesWPF/

├── App.xaml / App.xaml.cs
├── Models/
│   ├── ChatMessage.cs
│   ├── HermesProject.cs
│   └── SessionHistory.cs
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── ChatViewModel.cs
│   └── ProjectViewModel.cs
├── Views/
│   ├── MainWindow.xaml
│   ├── ChatView.xaml
│   ├── ProjectPanel.xaml
│   └── TerminalView.xaml
├── Services/
│   ├── HermesService.cs
│   ├── ProjectService.cs
│   └── HistoryService.cs
├── Commands/
│   └── RelayCommand.cs
└── Resources/
    ├── Styles.xaml
    └── Icons.xaml
```

---

## 3. Промпт для Cursor

Скопируй и вставь в Cursor:

```text
You are an expert C# / WPF / MVVM developer. Create a complete WPF application
called HermesWPF for .NET 8...

[ОСТАВЛЕН БЕЗ ИЗМЕНЕНИЙ — ВСТАВЛЯЕТСЯ ПОЛНОСТЬЮ]
```

*(оставь оригинальный блок без изменений — он уже корректный для Cursor)*

---

## 4. Ключевые фрагменты кода

### ⚙️ HermesService — SendMessageAsync

```csharp
public async Task<string> SendMessageAsync(string message, string wslWorkDir)
{
    var escaped = message.Replace("'", "\\'").Replace('"', '\\"');

    var args = string.IsNullOrEmpty(wslWorkDir)
        ? $"-e bash -c \"source ~/hermes-agent/venv/bin/activate && hermes chat -z '{escaped}'\""
        : $"--cd \"{wslWorkDir}\" -e bash -c \"source ~/hermes-agent/venv/bin/activate && hermes chat -z '{escaped}'\"";

    var psi = new ProcessStartInfo {
        FileName = "wsl.exe",
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8
    };

    using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
    var sb = new StringBuilder();

    process.OutputDataReceived += (_, e) => {
        if (e.Data is null) return;
        sb.AppendLine(e.Data);
        OutputReceived?.Invoke(e.Data);
    };

    process.Start();
    process.BeginOutputReadLine();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await process.WaitForExitAsync(cts.Token);

    return sb.ToString().Trim();
}
```

---

### ⚙️ RelayCommand

```csharp
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    { 
        _execute = execute; 
        _canExecute = canExecute; 
    }

    public event EventHandler? CanExecuteChanged {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
    public void Execute(object? p) => _execute(p);
}
```

---

### 🔀 ConvertToWslPath

```csharp
public static string ConvertToWslPath(string windowsPath)
{
    if (string.IsNullOrEmpty(windowsPath)) return string.Empty;

    var normalized = windowsPath.Replace("\\", "/");

    if (normalized.Length >= 2 && normalized[1] == ':')
    {
        var drive = char.ToLower(normalized[0]);
        var rest  = normalized[2..].TrimStart('/');
        return $"/mnt/{drive}/{rest}";
    }

    return normalized;
}
```

---

## 5. План реализации

### Phase 1 — Scaffolding (30 мин)

* Создать проект
* Добавить структуру папок
* Подключить JSON и стили

### Phase 2 — Services (45 мин)

* Реализовать HermesService
* ProjectService
* HistoryService

### Phase 3 — ViewModel (30 мин)

* MainViewModel
* Команды
* Загрузка истории

### Phase 4 — UI (60 мин)

* Три колонки
* Чат + проекты + терминал

### Phase 5 — Polish (30 мин)

* Стили
* Автоскролл
* Проверка WSL
* Обработка ошибок

---

## 6. Кнопки быстрых действий

| Кнопка          | Команда                       | Описание       |
| --------------- | ----------------------------- | -------------- |
| ▶ Gateway Run   | `gateway run`                 | Запуск шлюза   |
| ◉ Status        | `status`                      | Статус агента  |
| ↺ Reset Webhook | `gateway reset-webhook`       | Сброс webhook  |
| ⬡ Analyze Code  | `chat -z "Проанализируй код"` | Анализ проекта |

---

## 7. Поток данных

```
USER INPUT
    ↓
MainViewModel.SendCommand
    ↓
HermesService.SendMessageAsync
    ↓
WSL (hermes chat)
    ↓
OutputReceived → Terminal
    ↓
Result → Messages
    ↓
HistoryService.Save
    ↓
UI обновляется
```

---

Если хочешь, могу:

* 🔥 сделать **чистый README.md (GitHub-ready)**
* 🎨 или оформить как **документацию + архитектурную диаграмму**
* ⚡ или разбить на **несколько md-файлов (docs/ структура)**
