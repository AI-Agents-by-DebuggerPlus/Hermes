# Перемонтировать StartSttFromPump / StopStt на OfflineSttRecognizer

Файл: `Hermes.EnglishTutorClient/HeadsetTestWindow.xaml.cs`

## Цель

Заменить связку `PcmPumpStream` + `SpeechRecognitionEngine.SetInputToAudioStream(pump)`
(live-stream, три раунда правок так и не дали hyp/rec) на `OfflineSttRecognizer` из
`02_OfflineSttRecognizer.cs` (буферизация сегментов + одноразовый offline `Recognize()`).

`PcmPumpStream` при этом **не удалять** — он всё ещё может понадобиться для other live-use
cases в будущем (например, если позже перейдём на Windows.Media.SpeechRecognition, которому
живой поток не так проблематичен). Просто в тестовом окне он больше не используется как вход
для System.Speech.

## Что менять

### 1. Поле класса

Было (примерно):
```csharp
private PcmPumpStream _pcmPump;
private SpeechRecognitionEngine _engine;
```

Стало:
```csharp
private OfflineSttRecognizer _offlineStt;
```

### 2. OnCaptureData

Было: `_pcmPump.WritePcm(pcm16Bytes, 0, pcm16Bytes.Length);`

Стало: `_offlineStt?.WritePcm(pcm16Bytes, 0, pcm16Bytes.Length);`

(Оставить peak/EQ расчёт как есть — throttle-логику из `01_UpdateMetersUi_fix.md`
применить отдельно.)

### 3. StartSttFromPump → StartStt (переименовать по смыслу, раз pump больше не входная точка)

```csharp
private void StartStt()
{
    _offlineStt = new OfflineSttRecognizer(sampleRate: 16000, bitsPerSample: 16, channels: 1);
    _offlineStt.SegmentRecognized += text =>
    {
        Dispatcher.BeginInvoke(() => AppendStt($"[rec] {text}"));
        AppLog.Info($"HeadsetTest STT recognized: {text}");
    };
    _offlineStt.SegmentRejected += reason =>
    {
        AppLog.Info($"HeadsetTest STT segment rejected: {reason}");
    };
    _offlineStt.RecognitionError += ex =>
    {
        AppLog.Error($"HeadsetTest STT segment error: {ex}");
    };

    _offlineStt.Start(cultureName: "en-US"); // TODO: ru-RU follow-up, out of scope here
    AppLog.Info("HeadsetTest STT started (offline/segmented) via OfflineSttRecognizer");
}
```

### 4. StopStt — остаётся неблокирующим (фикс из предыдущего пасса сохраняется)

```csharp
private void StopStt()
{
    AppLog.Info("StopMicAsync begin (non-blocking)");
    _isSttRunning = false; // update UI button state from this flag as before

    var stt = _offlineStt;
    _offlineStt = null;
    Task.Run(() =>
    {
        try
        {
            stt?.Stop();
            stt?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error($"StopStt background cleanup failed: {ex}");
        }
    });
}
```

### 5. Все места, где раньше звался `StartSttFromPump()` — заменить на `StartStt()`.

Все места, где раньше проверялось состояние `_pcmPump`/`_engine` для UI (например, disable
кнопки Start пока идёт сессия) — переключить на `_offlineStt != null` / `_isSttRunning`.

## Как проверить

1. Собрать проект — не должно быть ссылок на удалённый `SetInputToAudioStream(pump)` в
   тестовом окне (в `PcmPumpStream.cs` класс остаётся нетронутым, просто не используется
   здесь).
2. Запустить тест, говорить фразы по 2–3 секунды с паузами.
3. В логе должны появляться `HeadsetTest STT recognized: ...` для сегментов, где было
   реальное произнесение, и `HeadsetTest STT segment rejected: ...` для тихих/невнятных
   сегментов — то есть впервые за три отчёта появится **хоть какая-то** реакция recognizer'а,
   не только тишина.
4. UI должен оставаться отзывчивым (проверить фикс из `01_UpdateMetersUi_fix.md` параллельно).
