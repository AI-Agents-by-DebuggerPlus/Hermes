# Диагностика: почему GlobalSystemMediaTransportControlsSessionManager.RequestAsync() → null

Затрагивает:
- `Hermes.EnglishTutorClient/Services/MediaFocusClaimer.cs` (метод `LogCompetingSessionsAsync`,
  добавленный в Fix Pass #4)

## Проблема

Лог показывает `Competing sessions check: manager null` — то есть сам вызов
`await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` либо возвращает null,
либо (более вероятно) кидает исключение, которое сейчас проглатывается/логируется недостаточно
подробно, и код интерпретирует это как "manager null" без деталей.

Это блокирует проверку гипотезы P1 (перехватывает ли Play другое приложение) — без рабочей
диагностики мы не можем отличить "фокус реально уходит в другое приложение" от "наш API-вызов
сам сломан".

## Возможные причины (по вероятности)

| # | Причина | Как проверить |
|---|---------|----------------|
| D1 | Вызов идёт не с UI/STA-потока, а WinRT `GlobalSystemMediaTransportControlsSessionManager` требует правильного apartment state | Обернуть вызов явным логом текущего `Thread.CurrentThread.GetApartmentState()` |
| D2 | Приложение не упаковано как MSIX/не имеет нужного package identity — часть WinRT Media API на голом .NET (non-packaged) Win32-приложении может не работать вообще | Проверить, как приложение запущено — packaged или unpackaged .exe |
| D3 | Исключение реально выбрасывается, но `catch` слишком широкий и превращает его в тихий null без деталей | Проверить текущий код `LogCompetingSessionsAsync` — вероятно там `catch (Exception) { return null; }` без лога |
| D4 | API вызван до полной инициализации WinRT runtime в процессе (слишком рано при старте) | Попробовать вызвать не при старте, а по требованию (например, при первом неудачном Play) |

## Что сделать

### 1. Логировать реальное исключение, а не просто "manager null"

Текущий код (судя по прошлому пассу) выглядит примерно так:
```csharp
try
{
    var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    var current = manager.GetCurrentSession();
    AppLog.Info($"Competing sessions check: current system session = {current?.SourceAppUserModelId ?? "(none)"}");
}
catch (Exception ex)
{
    AppLog.Error($"LogCompetingSessionsAsync failed: {ex}");
}
```

Если сейчас лог реально показывает `manager null` без стектрейса — либо `RequestAsync()`
вернул null без исключения (проверить это явно), либо исключение теряется где-то выше по
call stack. Явно разделить оба случая:

```csharp
GlobalSystemMediaTransportControlsSessionManager manager = null;
try
{
    manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
}
catch (Exception ex)
{
    AppLog.Error($"LogCompetingSessionsAsync: RequestAsync threw: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    return;
}

if (manager == null)
{
    AppLog.Error("LogCompetingSessionsAsync: RequestAsync() returned null WITHOUT throwing — " +
                  "possible packaging/apartment-state issue (see 02_SMTC_Manager_Null_Diagnostics.md D1/D2)");
    return;
}

var current = manager.GetCurrentSession();
AppLog.Info($"Competing sessions check: current system session = {current?.SourceAppUserModelId ?? "(none)"}");
```

### 2. Проверить apartment state в момент вызова

```csharp
AppLog.Info($"LogCompetingSessionsAsync: thread apartment state = {Thread.CurrentThread.GetApartmentState()}, " +
            $"is STA = {Thread.CurrentThread.GetApartmentState() == ApartmentState.STA}");
```

WinRT media session APIs обычно ожидают STA. Если поток здесь MTA — это, вероятно, и есть
причина (D1). Если так — вызывать `LogCompetingSessionsAsync` явно через
`Dispatcher.InvokeAsync` (UI-поток обычно STA в WPF) вместо фонового `Task.Run`.

### 3. Проверить packaging (D2)

Если приложение — обычный unpackaged `.exe` (не MSIX), часть WinRT API из
`Windows.Media.Control` namespace может требовать package identity, чтобы система вообще
согласилась выдать `SessionManager`. Быстрая проверка — залогировать
`Windows.ApplicationModel.Package.Current` в try/catch на старте:

```csharp
try
{
    var pkg = Windows.ApplicationModel.Package.Current;
    AppLog.Info($"Package identity present: {pkg.Id.FullName}");
}
catch (Exception ex)
{
    AppLog.Error($"No package identity (unpackaged app?) — {ex.GetType().Name}: {ex.Message}. " +
                 "This may explain GlobalSystemMediaTransportControlsSessionManager returning null.");
}
```

Если это подтвердится (D2) — это архитектурное ограничение, не быстрый код-фикс: либо
паковать приложение как MSIX (Sparse Package достаточно для доступа к части WinRT API без
полной MSIX-дистрибуции), либо отказаться от этой конкретной диагностики и полагаться только
на логи самого Tutor (SMTC subscribe/button events), без сравнения с чужими сессиями.

## Как проверить

1. Запустить приложение, посмотреть новый подробный лог `LogCompetingSessionsAsync` при
   старте — должно быть видно либо реальное исключение с деталями, либо явное подтверждение
   apartment state / package identity проблемы, либо (в лучшем случае) наконец рабочий
   `current system session = ...`.
2. Если после этого diagnostics всё ещё "null без причины" — записать в отчёт находки по
   D1–D4 буквально, чтобы решить, стоит ли вообще чинить эту диагностику дальше, или
   переключиться на другой способ проверки гипотезы P1 (например, просто эмпирически: закрыть
   Chrome и другие медиа-приложения перед тестом Play и посмотреть, изменится ли поведение).
