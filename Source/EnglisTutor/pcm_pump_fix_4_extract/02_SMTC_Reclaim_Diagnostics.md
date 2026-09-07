# Диагностика + re-claim SMTC Playing после reclaim аудио-эндпоинта

Затрагивает:
- `Hermes.EnglishTutorClient/Services/MediaFocusClaimer.cs`
- `Hermes.EnglishTutorClient/Services/HeadsetAudioClaimer.cs` (только точка вызова re-claim,
  не менять саму claim/hold логику)
- `Hermes.EnglishTutorClient/Services/MediaPlayHotkey.cs` (только логирование)

## Проблема

За сессию 21:03–21:06 в логе нет ни одной строки:
- `Media focus SMTC button: Play/Pause`
- `MediaPlay: LL hook VK_MEDIA_PLAY_PAUSE`
- `MediaPlay: WM_HOTKEY`

Раньше (в других сессиях) SMTC срабатывал — значит подписка не сломана навсегда, это гонка
за фокус или потеря подписки после конкретного события (скорее всего — reclaim аудио
default-эндпоинта после теста mic, когда `HeadsetAudioClaimer` возвращает hold на Stereo).

**Важно понимать масштаб фикса:** этот блок в первую очередь **диагностический**. Причина
может быть в самом Tutor (потерянная подписка) — это чинится. Но причина может быть и в том,
что другое приложение (например Chrome — гипотеза P1 из отчёта) держит системный медиафокус
и Windows отдаёт AVRCP Play ему, а не Tutor — это **не чинится** внутри Tutor кода, там нужно
будет либо явно перехватывать фокус агрессивнее, либо это принимается как ограничение
платформы. Цель этого пасса — получить достаточно логов, чтобы понять, какой из двух случаев
происходит.

## Что сделать

### 1. Логировать каждую попытку получить/потерять SMTC-сессию

В `MediaFocusClaimer.cs`, в `Start()` / `TryEnableSmtc()` и везде, где подписывается
`ButtonPressed`:

```csharp
AppLog.Info("MediaFocusClaimer: attempting SMTC session claim");
// ... existing SystemMediaTransportControls setup ...
AppLog.Info($"MediaFocusClaimer: SMTC session claimed, PlaybackStatus={_smtc.PlaybackStatus}");
```

При подписке кнопки:
```csharp
_smtc.ButtonPressed += OnButtonPressed;
AppLog.Info("MediaFocusClaimer: ButtonPressed handler subscribed");
```

В обработчике — логировать *любое* срабатывание, даже если потом отфильтровывается:
```csharp
private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
{
    AppLog.Info($"MediaFocusClaimer: SMTC button pressed: {args.Button}");
    // ... existing logic ...
}
```

### 2. Явный re-claim после любого reclaim аудио-эндпоинта

Найти место в `HeadsetAudioClaimer.cs`, где после паузы hold (для mic-теста) происходит
`Reclaim()`/resume на Stereo — это уже упоминается в предыдущих отчётах
(`hold paused for mic` / `hold resumed`). Сразу после успешного reclaim добавить вызов в
`MediaFocusClaimer`, форсирующий:

```csharp
// In HeadsetAudioClaimer, right after Reclaim()/hold resume succeeds:
AppLog.Info("HeadsetAudioClaimer: reclaim complete — re-asserting SMTC Playing status");
_mediaFocusClaimer?.ReassertPlayingStatus();
```

Добавить метод в `MediaFocusClaimer.cs`:
```csharp
public void ReassertPlayingStatus()
{
    if (_smtc == null)
    {
        AppLog.Error("MediaFocusClaimer.ReassertPlayingStatus: SMTC session is null — was it ever claimed?");
        return;
    }
    try
    {
        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        // Re-subscribe defensively in case the previous subscription was silently dropped
        // by the OS during the endpoint change:
        _smtc.ButtonPressed -= OnButtonPressed;
        _smtc.ButtonPressed += OnButtonPressed;
        AppLog.Info("MediaFocusClaimer: PlaybackStatus reasserted to Playing, ButtonPressed re-subscribed");
    }
    catch (Exception ex)
    {
        AppLog.Error($"MediaFocusClaimer.ReassertPlayingStatus failed: {ex}");
    }
}
```

### 3. Логировать конкурирующие медиасессии (для проверки гипотезы P1)

Если проект уже использует (или может использовать) `GlobalSystemMediaTransportControlsSessionManager`
(WinRT API для перечисления активных медиасессий в системе), добавить разовый диагностический
лог при старте Tutor и при каждом неудачном Play-событии:

```csharp
private static async Task LogCompetingSessionsAsync()
{
    try
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var current = manager.GetCurrentSession();
        AppLog.Info($"Competing sessions check: current system session = " +
            $"{current?.SourceAppUserModelId ?? "(none)"}");
    }
    catch (Exception ex)
    {
        AppLog.Error($"LogCompetingSessionsAsync failed: {ex}");
    }
}
```

Вызвать это один раз при старте Tutor и, если удобно, каждый раз когда пользователь жмёт
Play на будсах, но событие Tutor не ловит (то есть — по watchdog-таймеру раз в N секунд,
если ожидается Play, а строки в логе нет; это опционально, не обязательно реализовывать,
если усложняет — можно ограничиться логом на старте).

## Как проверить

1. Запустить Tutor, дождаться `MediaFocusClaimer: SMTC session claimed`.
2. Пройти mic-тест (который делает pause/reclaim hold) — убедиться, что после reclaim в логе
   есть `re-asserting SMTC Playing status` и `PlaybackStatus reasserted to Playing`.
3. Нажать Play на гарнитуре:
   - Если в логе появилась `SMTC button pressed: Play` — подписка работала, проблема была
     именно в потере статуса/подписки после reclaim, и этот фикс её решает.
   - Если строки по-прежнему нет — смотреть `Competing sessions check` при старте: если там
     не Tutor, а другое приложение — это подтверждает P1 (фокус реально уходит не туда), и
     это отдельная, более системная проблема, которую стоит зафиксировать в отчёте, а не
     пытаться докрутить кодом в этом пассе.
