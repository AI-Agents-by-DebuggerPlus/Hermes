# Фикс: снять COM/meter с UI-потока + throttle + watchdog

Файл: `Hermes.EnglishTutorClient/HeadsetTestWindow.xaml.cs`

## Проблема

`UpdateMetersUi` (или его аналог — таймер/callback, обновляющий прогресс-бар/EQ каждые
~50 мс) обращается к `AudioMeterInformation.MasterPeakValue` или другому COM-объекту
Windows Core Audio **на UI-потоке**. Это COM-вызов к Bluetooth-эндпоинту — при нестабильном
HFP-линке он может синхронно заблокироваться внутри BT-стека драйвера, и весь WPF Dispatcher
встаёт. Это согласуется с логом: `peak=` продолжает писаться (фоновый capture-поток жив),
но UI полностью не отвечает и клик Stop не доходит до обработчика.

Отдельно: `OnCaptureData` дергает `Dispatcher.BeginInvoke(AppendStt)` на **каждый** peak
(每секунду), раздувая TextBox и очередь диспетчера — это не сам deadlock, но маскирует и
усугубляет H1.

## Что сделать

### 1. Убрать COM-meter вызовы с UI-потока

- Найти таймер/callback (`DispatcherTimer` или похожий), который читает
  `AudioMeterInformation` / любой `IAudioMeterInformation` COM-интерфейс.
- Peak уже считается на фоновом потоке в `OnCaptureData` (переменная типа `_inPeak` /
  `_currentPeak`) — UI должен читать **только это значение**, никогда не COM-объект напрямую.
- Если update прогресс-бара всё равно нужен через Dispatcher — передавать туда **только
  число** (`double`), не делать внутри `BeginInvoke`-колбэка никаких COM-вызовов:

```csharp
// UI timer callback — safe version, no COM calls here
private void UiMeterTimer_Tick(object sender, EventArgs e)
{
    // _currentPeak is a field updated by the background capture thread
    // (Interlocked / volatile double), never touched via COM on this thread.
    MeterBar.Value = Volatile.Read(ref _currentPeakBits); // or your existing safe accessor
}
```

### 2. Throttle логирования peak в TextBox

- В `OnCaptureData`, вместо `Dispatcher.BeginInvoke(AppendStt, ...)` на каждый вызов —
  писать в `AppLog` (файл) как и сейчас, но в UI TextBox — не чаще раз в 5 секунд:

```csharp
private DateTime _lastUiPeakLog = DateTime.MinValue;

private void OnCaptureData(...)
{
    // ... existing peak calc, WritePcm, AppLog.Info($"peak={peak} ...") unchanged ...

    var now = DateTime.UtcNow;
    if ((now - _lastUiPeakLog).TotalSeconds >= 5)
    {
        _lastUiPeakLog = now;
        var snapshot = peak; // capture value for closure
        Dispatcher.BeginInvoke(() => AppendStt($"peak={snapshot:F3}"));
    }
}
```

### 3. Watchdog на случай, если Dispatcher всё равно подвиснет

Добавить независимый фоновый таймер (System.Threading.Timer, НЕ DispatcherTimer — он сам
зависит от того же Dispatcher), который проверяет, отвечает ли UI, и если `_micOn` активен
дольше N секунд без реакции — форсированно останавливает capture из фонового потока:

```csharp
private System.Threading.Timer _watchdog;
private volatile bool _micOn;
private DateTime _micStartedAt;

private void StartWatchdog()
{
    _watchdog = new System.Threading.Timer(_ =>
    {
        if (_micOn && (DateTime.UtcNow - _micStartedAt) > TimeSpan.FromSeconds(120))
        {
            AppLog.Error("Watchdog: mic session exceeded 120s without stop — forcing SignalEnd from background thread");
            _pcmPump?.SignalEnd();
            _micOn = false;
        }
    }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
}
```

Вызвать `StartWatchdog()` один раз при инициализации окна теста (или при старте mic, как
удобнее по существующей структуре класса).

## Как проверить, что фикс сработал

1. Запустить тест, говорить в микрофон 20–30 секунд.
2. UI (кнопки, прогресс-бар) должен оставаться отзывчивым весь период — можно двигать окно,
   жать другие элементы.
3. Клик Stop должен мгновенно дать в логе `StopMicAsync begin`.
4. Если UI всё же подвиснет дольше 2 минут — watchdog должен сам остановить capture и
   записать `Watchdog: mic session exceeded...` в лог.
