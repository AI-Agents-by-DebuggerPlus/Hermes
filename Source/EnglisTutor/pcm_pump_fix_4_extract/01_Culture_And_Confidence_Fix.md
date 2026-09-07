# Фикс: правильный выбор recognizer по culture + порог confidence

Затрагивает:
- `Hermes.EnglishTutorClient/Services/VoiceInputService.cs` (метод выбора recognizer —
  судя по логу, называется что-то вроде `PickRecognizer`)
- новый класс из Fix Pass #3 — `OfflineSttRecognizer` (используется в
  `HeadsetTestWindow.xaml.cs`)

## Проблема

Лог показывает:
```
Voice toggle: START … cultureReq=ru-RU
STT recognizer=… (English - US) culture=en-US    ← запрос ru, движок en
```
То есть запрошенная культура нигде не долетает до реального выбора `RecognizerInfo` — либо
метод выбора игнорирует параметр, либо на машине физически не установлен ru-RU recognizer и
код молча берёт первый/дефолтный вместо явного варнинга.

## Что сделать в `VoiceInputService.cs`

1. Найти место, где создаётся `SpeechRecognitionEngine` (или `RecognizerInfo` выбирается) —
   вероятно, в методе `PickRecognizer` или прямо в `Start()`.
2. Явно перечислить `SpeechRecognitionEngine.InstalledRecognizers()` и фильтровать по
   `Culture.Name == cultureReq` (например `"ru-RU"`):

```csharp
private static RecognizerInfo PickRecognizer(string cultureReq)
{
    var installed = SpeechRecognitionEngine.InstalledRecognizers();

    var exact = installed.FirstOrDefault(r =>
        string.Equals(r.Culture.Name, cultureReq, StringComparison.OrdinalIgnoreCase));

    if (exact != null)
    {
        AppLog.Info($"PickRecognizer: using exact match for {cultureReq}: {exact.Name}");
        return exact;
    }

    // No exact match installed — log this loudly, don't silently fall back.
    var fallback = installed.FirstOrDefault();
    AppLog.Error(
        $"PickRecognizer: NO installed recognizer for cultureReq={cultureReq}. " +
        $"Installed cultures: {string.Join(", ", installed.Select(r => r.Culture.Name))}. " +
        $"Falling back to {fallback?.Culture.Name ?? "NONE"}.");
    return fallback;
}
```

3. Use the returned `RecognizerInfo` to construct the engine:
```csharp
var info = PickRecognizer(cultureReq);
if (info == null)
{
    AppLog.Error("PickRecognizer: no recognizers installed at all — aborting STT start.");
    return;
}
_engine = new SpeechRecognitionEngine(info.Id);
```

4. **Важно:** если ru-RU физически не установлен на машине — это не баг кода, а окружения.
   Проверить `SpeechRecognitionEngine.InstalledRecognizers()` при старте приложения (например,
   в логе при инициализации), чтобы сразу было видно, стоит ли вообще ru-RU языковой пакет
   Windows Speech. Если нет — это отдельная задача (поставить пакет), не код-фикс.

## Confidence threshold — оба пути (live и offline)

### В `VoiceInputService.cs`, в обработчике `SpeechRecognized`:

```csharp
private const float MinConfidence = 0.5f;

private void OnRecognized(object sender, SpeechRecognizedEventArgs e)
{
    if (e.Result == null || e.Result.Confidence < MinConfidence)
    {
        AppLog.Info($"Recognized but below confidence threshold: conf={e.Result?.Confidence:F2} text={e.Result?.Text}");
        return; // do not forward to UI / Supabase
    }
    // ... existing forward-to-UI/Supabase logic ...
}
```

### В `OfflineSttRecognizer` (файл из Fix Pass #3, `02_OfflineSttRecognizer.cs` в том пакете):

В методе `RecognizeSegment`, после `var result = engine.Recognize();`:

```csharp
var result = engine.Recognize();
if (result != null && result.Confidence >= MinConfidence && !string.IsNullOrWhiteSpace(result.Text))
{
    SegmentRecognized?.Invoke(result.Text);
}
else
{
    SegmentRejected?.Invoke(result == null
        ? "(no match)"
        : $"(below confidence: {result.Confidence:F2}, text={result.Text})");
}
```

Добавить `MinConfidence` как параметр конструктора `OfflineSttRecognizer` (default 0.5f), и
пробросить `cultureName` так же, как culture пробрасывается в `VoiceInputService` — то есть
`OfflineSttRecognizer.Start(cultureName)` должен получать реальный `cultureReq`, а не
хардкод `"en-US"`. Внутри `RecognizeSegment` использовать `PickRecognizer`-подобную логику
(или переиспользовать общий helper, если он появится в `VoiceInputService`, чтобы не
дублировать выбор recognizer'а в двух местах).

## Как проверить
1. Проверить лог инициализации — должен явно показать список `InstalledRecognizers` и что
   выбрано для `ru-RU`.
2. Сказать фразу на русском — либо распознаётся ru-RU движком (если установлен), либо в логе
   явно видно предупреждение про отсутствие ru-RU (а не молчаливый en-US).
3. Сказать что-то невнятное/шум — должно уйти в `SegmentRejected`/`below confidence`, а не
   попасть в UI/Supabase как случайный английский текст.
