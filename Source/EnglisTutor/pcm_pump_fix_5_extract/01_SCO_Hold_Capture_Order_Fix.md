# Фикс: SCO hold ломает capture endpoint (0x88890008)

Затрагивает:
- `Hermes.EnglishTutorClient/HeadsetTestWindow.xaml.cs` (метод `StartMicAsync`, ~строка 320,
  и место, где вызывается `HeadsetTest SCO hold playback on ...`)
- `Hermes.EnglishTutorClient/Services/HeadsetAudioClaimer.cs` (`StartHoldPlayback` /
  `PauseHoldForCapture` — тот же SCO-hold механизм, если используется и в главном окне)

## Диагноз

Порядок вызовов в логе:
```
HeadsetTest SCO render Headset (… Hands-Free AG Audio)
HeadsetTest SCO hold playback on Headset (… Hands-Free AG Audio)   ← WasapiOut открывается ЗДЕСЬ
   ... 2 секунды ...
ERROR HeadsetTest capture: COMException (0x88890008)
   at IAudioClient.GetMixFormat
   at WasapiCapture..ctor(...)
```

`0x88890008` = `AUDCLNT_E_UNSUPPORTED_FORMAT`. Возникает на `GetMixFormat` внутри конструктора
`WasapiCapture`, то есть до всякого реального формата — это типичный симптом того, что
`MMDevice`, полученный до открытия SCO-hold render-потока, стал невалидным к моменту, когда
`WasapiCapture` пытается его использовать (переключение BT-профиля/повторная инициализация
endpoint между render-open и capture-open).

## Что сделать

### 1. Поменять порядок: сначала capture, потом (если вообще нужен) render-hold

Если `SCO hold playback` нужен только чтобы **удержать** Hands-Free профиль активным (чтобы
он не откатился обратно на A2DP Stereo до того как mic успеет открыться) — держать его нужно
**после**, а не **до** открытия capture. Открывать capture первым:

```csharp
// StartMicAsync — new order
await RefreshDevices();
PreferHandsFree();
await AudioPolicyConfig.TryClaimAllRoles(handsFreeDeviceId);

// 1. Open capture FIRST, while the endpoint is freshly claimed and not yet
//    touched by any other WASAPI client on the same device.
var captureDevice = GetFreshMMDevice(handsFreeDeviceId); // see helper below
_capture = new WasapiCapture(captureDevice);
_capture.DataAvailable += OnCaptureData;
_capture.StartRecording();
AppLog.Info("HeadsetTest capture start (WasapiCapture opened before SCO hold)");

// 2. THEN start the near-silent SCO-hold render, if still needed to prevent
//    the OS from dropping back to A2DP Stereo mid-session.
StartScoHoldPlaybackIfNeeded(captureDevice);
```

### 2. Получать свежий `MMDevice` непосредственно перед `new WasapiCapture`

Не переиспользовать `MMDevice`, полученный на этапе `RefreshDevices()`/`PreferHandsFree()` —
между тем моментом и открытием capture могло пройти достаточно времени (PolicyConfig calls,
SCO render setup), чтобы endpoint успел смениться. Получать заново прямо перед конструктором:

```csharp
private MMDevice GetFreshMMDevice(string deviceId)
{
    using var enumerator = new MMDeviceEnumerator();
    return enumerator.GetDevice(deviceId);
}
```

### 3. Защитный retry с задержкой, если 0x88890008 всё же случится

Даже после реордеринга — добавить один retry с коротким delay, т.к. BT-профиль переключение
не мгновенное:

```csharp
private async Task<WasapiCapture> OpenCaptureWithRetryAsync(string deviceId, int maxAttempts = 2)
{
    COMException lastEx = null;
    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            var device = GetFreshMMDevice(deviceId);
            var capture = new WasapiCapture(device);
            return capture;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x88890008)
        {
            lastEx = ex;
            AppLog.Error($"HeadsetTest capture: 0x88890008 attempt {attempt}/{maxAttempts}, retrying after delay");
            await Task.Delay(300);
        }
    }
    throw lastEx;
}
```

Использовать этот helper вместо прямого `new WasapiCapture(...)` и в тестовом окне, и в
главном (`VoiceInputService`), если там тот же паттерн SCO-hold-перед-capture (судя по логу
отчёта #6, `peak=0.000` в главном окне с большой вероятностью та же причина — стоит применить
фикс в обоих местах и проверить оба).

### 4. Не открывать SCO-hold, если capture так и не открылся

Если после retry всё равно исключение — не пытаться стартовать `StartScoHoldPlaybackIfNeeded`,
сразу идти в `StopMicAsync`/аналог с явным логом причины (уже частично работает — `StopMicAsync`
вызывается в catch, просто нужно убедиться, что SCO-hold render к этому моменту не запущен и
не оставлен висеть).

## Как проверить

1. Запустить тест гарнитуры 3+ раза подряд — ни разу не должно быть `0x88890008`.
2. `peak=` должен появляться в логе (не `peakMax=0.000`), EQ в UI реально шевелится.
3. В главном окне повторить `Voice toggle: START`, сказать фразу — проверить, что `STT meter`
   показывает `peak > 0`, а не мгновенный `Internal error`.
4. Если retry реально сработал хоть раз (лог `retrying after delay`) — это сигнал, что
   переключение BT-профиля просто медленнее, чем ожидалось, и, возможно, стоит увеличить
   базовую задержку перед первым открытием capture, а не полагаться только на retry.
