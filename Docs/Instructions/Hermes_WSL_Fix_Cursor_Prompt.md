# Cursor Prompt — Fix WSL `execvpe(bash) failed` Error

## Контекст

В логах повторяется одна и та же ошибка:

```
WSL ERROR: CreateProcessCommon:800: execvpe(bash) failed: No such file or directory
```

Это означает что WSL не может найти `bash` при запуске через `-e bash`.  
**Причина:** флаг `-e` требует абсолютного пути к исполняемому файлу внутри дистрибутива.  
WSL ищет буквально `bash` без `PATH`, и не находит его.

---

## Что нужно исправить в `ConnectionService.cs` и `HermesService.cs`

### Правило: никогда не использовать `-e bash`

```csharp
// ❌ НЕПРАВИЛЬНО — bash не найден через -e
Arguments = $"-d \"{distro}\" -e bash -lc \"source ~/venv/bin/activate && hermes status\""

// ✅ ПРАВИЛЬНО — передавать команду через wsl напрямую со строкой
Arguments = $"-d \"{distro}\" -- /bin/bash -lc \"source ~/venv/bin/activate && hermes status\""

// ✅ ТАКЖЕ ПРАВИЛЬНО — без указания дистрибутива если он default
Arguments = $"-- /bin/bash -lc \"source ~/venv/bin/activate && hermes status\""
```

**Ключевое изменение:** `-e bash` → `-- /bin/bash -lc`

---

## Задача для Cursor

Найди в проекте `Hermes.Wpf` **все** места где используется:
- `-e bash`
- `-e /bin/bash`  
- `bash -lc`
- `bash -c`

...и замени по следующим правилам:

### Правило замены

| Было | Стало |
|------|-------|
| `-d "{distro}" -e bash -lc "{cmd}"` | `-d "{distro}" -- /bin/bash -lc "{cmd}"` |
| `-d "{distro}" -e bash -c "{cmd}"` | `-d "{distro}" -- /bin/bash -c "{cmd}"` |
| `-e bash -lc "{cmd}"` (без distro) | `-- /bin/bash -lc "{cmd}"` |
| `-e bash -c "{cmd}"` (без distro) | `-- /bin/bash -c "{cmd}"` |

### Вспомогательный метод — добавить в `HermesService.cs`

Создай приватный хелпер, который строит аргументы для `wsl.exe` единообразно:

```csharp
/// <summary>
/// Builds wsl.exe arguments that reliably find /bin/bash on any WSL distro.
/// Never use -e bash — it fails when PATH is not set in relay context.
/// </summary>
private string BuildWslArgs(string bashCommand, string? wslWorkDir = null)
{
    var distro = _settings.WslDistro; // e.g. "Ubuntu" or "Ubuntu-22.04"

    // cd prefix if workdir specified
    var cdPrefix = string.IsNullOrEmpty(wslWorkDir)
        ? ""
        : $"cd '{wslWorkDir}' && ";

    var fullCmd = $"{cdPrefix}{bashCommand}";

    // Escape double quotes inside the bash command
    var escaped = fullCmd.Replace("\"", "\\\"");

    if (!string.IsNullOrEmpty(distro))
        return $"-d \"{distro}\" -- /bin/bash -lc \"{escaped}\"";
    else
        return $"-- /bin/bash -lc \"{escaped}\"";
}
```

### Использование хелпера

```csharp
// Было:
var psi = new ProcessStartInfo {
    FileName = "wsl.exe",
    Arguments = $"-d \"{distro}\" -e bash -lc \"source ~/hermes-agent/venv/bin/activate && hermes status\""
};

// Стало:
var psi = new ProcessStartInfo {
    FileName = "wsl.exe",
    Arguments = BuildWslArgs("source ~/hermes-agent/venv/bin/activate && hermes status")
};
```

---

## Дополнительно: улучшить WSL-проверку в `ConnectionService.cs`

Проверка WSL (CheckResult #1) тоже должна использовать надёжный вызов:

```csharp
// Проверка доступности WSL — не требует bash вообще
var psi = new ProcessStartInfo {
    FileName = "wsl.exe",
    Arguments = "--status",          // wsl.exe --status не требует bash
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
    StandardOutputEncoding = System.Text.Encoding.UTF8,
    StandardErrorEncoding = System.Text.Encoding.UTF8,
};

// Проверка venv — через /bin/bash
var venvCheck = BuildWslArgs($"test -d {_settings.VenvPath} && echo OK");

// Проверка hermes --version
var hermesVersion = BuildWslArgs(
    $"source {_settings.VenvPath}/bin/activate && {_settings.HermesCommand} --version");
```

---

## Дополнительно: определить правильный дистрибутив автоматически

Добавь в `ConnectionService.cs` метод для автоопределения дистрибутива:

```csharp
/// <summary>
/// Returns the name of the default WSL distro (excluding docker-desktop).
/// Falls back to empty string if not determinable.
/// </summary>
public static async Task<string> DetectDefaultDistroAsync()
{
    try
    {
        var psi = new ProcessStartInfo {
            FileName = "wsl.exe",
            Arguments = "--list --quiet",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.Unicode // wsl --list outputs UTF-16
        };

        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();

        // Skip docker-desktop and docker-desktop-data
        var distros = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimEnd('*').Trim()) // '*' marks default
            .Where(l => !string.IsNullOrEmpty(l)
                     && !l.StartsWith("docker", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return distros.FirstOrDefault() ?? string.Empty;
    }
    catch
    {
        return string.Empty;
    }
}
```

**Вызов:** в `SetupWizardViewModel` при завершении шага 1 (WSL найден):

```csharp
if (wslCheck.Ok && string.IsNullOrEmpty(_settings.WslDistro))
{
    _settings.WslDistro = await ConnectionService.DetectDefaultDistroAsync();
    _settingsService.Save(_settings);
}
```

---

## Checklist для Cursor

- [ ] Найти все `-e bash` и `-e /bin/bash` в проекте → заменить на `-- /bin/bash -lc`
- [ ] Добавить `BuildWslArgs()` в `HermesService.cs`
- [ ] Перевести все WSL-вызовы в `HermesService` и `ConnectionService` на `BuildWslArgs()`
- [ ] Добавить `DetectDefaultDistroAsync()` в `ConnectionService`
- [ ] Вызвать автоопределение дистрибутива в `SetupWizardViewModel` после успешной WSL-проверки
- [ ] Проверить что `wsl.exe --status` (без bash) используется для проверки доступности WSL
- [ ] Убедиться что `StandardOutputEncoding = Encoding.Unicode` для `wsl --list --quiet`
