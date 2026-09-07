# Hermes.EnglishTutorClient — Fix Pass #3: UI hang + переход на offline STT

Это четвёртый заход. Первые три отчёта показали:
1. Крэш `PcmPumpStream.Length` — исправлен.
2. `peak>0`, но SAPI молчит; hang на Stop — частично исправлен (silence padding убран,
   `StopStt` неблокирующий), но UI всё равно виснет **во время** listen, ещё до клика Stop.
3. UI виснет с самого начала listen-сессии (COM meter на UI-потоке — подозреваемый),
   а SAPI live-stream в принципе не даёт hyp/rec даже при живом сигнале.

Этот пасс решает оба вопроса одним заходом:

- **UI unblock**: убрать COM-вызовы (`AudioMeterInformation`) с UI-потока, throttle записи
  peak в TextBox, добавить watchdog на случай, если Dispatcher всё равно подвиснет.
- **Отказ от live SAPI recognize**: вместо потокового `SetInputToAudioStream(pump)` — копить
  PCM-сегменты (1–2 сек) в буфер, писать временный WAV, звать offline `Recognize()`. Это
  убирает саму причину, почему SAPI не видит поток как речь.

## Порядок действий для агента (Cursor)

1. Открыть `cursor_prompt.txt` — это единственный файл, который нужно скормить агенту как
   инструкцию. Он уже содержит весь контекст и ссылки на остальные файлы пакета.
2. Агент должен сам найти реальные файлы в репозитории (`PcmPumpStream.cs`,
   `HeadsetTestWindow.xaml.cs`, `VoiceInputService.cs` и т.д.) — приложенные `.cs`/`.patch`
   файлы в этом zip являются **референсной реализацией**, а не готовым diff'ом один-в-один
   (мы не видели актуальные файлы проекта после трёх раундов правок), так что агент обязан
   адаптировать код к текущей структуре класса, а не слепо копировать.
3. Файлы в пакете:
   - `01_UpdateMetersUi_fix.md` — что менять в `HeadsetTestWindow.xaml.cs` для снятия COM
     с UI-потока + throttle + watchdog.
   - `02_OfflineSttRecognizer.cs` — новый класс-обёртка: копит PCM во временный WAV,
     вызывает offline `Recognize()`, заменяет live `SetInputToAudioStream(pump)`.
   - `03_StartSttFromPump_rewrite.md` — как перемонтировать `StartSttFromPump`/`StopStt`
     на новый offline-recognizer вместо pump+live-stream.
   - `cursor_prompt.txt` — промпт для агента, включая требование собрать проект, прогнать
     ручной тест и **самостоятельно написать отчёт** в `EnglishTutorClient_Fix3_Report.md`,
     если что-то не сойдётся (билд не собрался, тест не прошёл, hang не устранён и т.п.).
4. После того как агент отработает — либо получаем рабочий тест, либо
   `EnglishTutorClient_Fix3_Report.md` с описанием, что именно не сошлось (по формату
   предыдущих трёх отчётов, чтобы можно было продолжить диагностику).

## Что НЕ трогать в этом пассе

- `AudioPolicyConfig.cs`, `HeadsetAudioClaimer.cs`, `MediaFocusClaimer.cs` — не менять.
- Recognizer culture (`en-US` vs `ru-RU`) — оставить TODO, отдельный follow-up.
- Боевой путь `VoiceInputService.Start` (`SetInputToDefaultAudioDevice`) — не трогать в этом
  пассе; если offline-recognizer в тестовом окне окажется рабочим, перенос в
  `VoiceInputService` — отдельная задача следующим шагом.
