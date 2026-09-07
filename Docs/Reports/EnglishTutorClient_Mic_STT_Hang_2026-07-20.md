# Отчёт #2: English Tutor Client — нет реакции на голос + зависание UI

**Дата:** 2026-07-20 (~20:23–20:25)  
**Проект:** `Hermes.EnglishTutorClient`  
**Лог:** `%LOCALAPPDATA%\Hermes.EnglishTutorClient\logs\app.log`  
**Предыдущий отчёт:** [`EnglishTutorClient_Mic_STT_Diagnosis_2026-07-20.md`](EnglishTutorClient_Mic_STT_Diagnosis_2026-07-20.md)

---

## Вердикт

1. **Микрофон иногда отдаёт сигнал** (`peak` до **0.522**) — захват WASAPI живой.  
2. **SAPI ни разу не распознал** — в логе нет `hyp` / `rec` / `rejected`.  
3. **UI зависает во время mic+STT** — в логе нет `StopMicAsync begin` при попытке стопа; при этом `peak=` продолжает писаться с фонового потока захвата → **UI мёртв, capture-поток жив**.

---

## 1. Факты из лога (после clear 20:23:50)

| Время | Событие |
|-------|---------|
| 20:23:53 | Тест: OUT=Pixel Buds **Stereo**, IN=Hands-Free **Active** |
| 20:23:53 | `hold paused for mic (release A2DP Stereo)` |
| 20:23:54 | SCO render = Hands-Free, PolicyConfig `hr=0` |
| 20:23:56 | WASAPI `16 kHz float mono`, `SetInputToAudioStream OK`, STT started |
| 20:23:56–58 | `peak≈0` |
| **20:23:59–24:00** | **`peak=0.076…0.155`, `max=0.522`** — голос/звук был |
| 20:24:01–25:14+ | Снова `peak≈0.000`, `bytes=2560–3200` (тихие буферы идут) |
| — | **Нет** `HeadsetTest STT hyp/rec` |
| — | **Нет** `StopMicAsync begin/done` |

Процесс на момент проверки: `Responding=True` (после перезапуска/восстановления), Id менялся между сессиями.

---

## 2. Две отдельные проблемы

### A. Нет реакции на голос (STT)

**Цепочка сейчас:**

```
WasapiCapture.DataAvailable
  → ToPcm16Mono → PcmPumpStream.WritePcm
  → SpeechRecognitionEngine.SetInputToAudioStream(pump)
  → RecognizeAsync(Multiple)
```

**Что работает:** PolicyConfig, выбор Hands-Free, WASAPI callbacks, pump, `SetInputToAudioStream`.  
**Что не работает:** события `SpeechHypothesized` / `SpeechRecognized` / (часто) даже осмысленный `AudioState` в app.log.

**Вероятные причины (по приоритету):**

| # | Причина | Почему похоже |
|---|---------|----------------|
| A1 | `PcmPumpStream.Read` подмешивает **silence tick** (нули), когда очередь пуста | SAPI видит «рваный» поток: речь + искусственная тишина → endpointing ломается, гипотез нет |
| A2 | После короткого пика Hands-Free снова **молчит** (`peak→0`) | BT HFP нестабилен при одновременном Stereo в системе (`OUT` после refresh снова Stereo) |
| A3 | Recognizer **en-US Dictation**, речь скорее **ru** | Даже при сигнале — низкая/нулевая отдача; обычно были бы `rejected`, их тоже нет → ближе к A1/A4 |
| A4 | SAPI плохо ест **live non-seekable** stream с «виртуальным» `Length` | Конструктор проходит, но движок может не вести нормальный dictation с pump |
| A5 | Боевой путь `VoiceInputService` всё ещё `SetInputToDefaultAudioDevice` | Не использует WASAPI-pump; при Play — отдельный путь с `Silence→Stopped` циклом |

### B. Зависание приложения (UI)

**Симптом:** кнопка «Стоп mic» / весь UI не реагирует; лог `peak=` продолжает идти.

**Механизм (подтверждённый ранее + этот прогон):**

```
UI thread                          SAPI / worker
─────────                          ─────────────
StopStt / Dispose(engine)  ←──ждёт──  Read(PcmPumpStream) блокируется
     ↑                                      │
     └──────── deadlock / долгий wait ──────┘
```

Частично смягчено (`SignalEnd`, `StopMicAsync`, dispose в `Task.Run`), но:

| # | Остаточный риск зависания | Где |
|---|---------------------------|-----|
| B1 | `StopStt()` всё ещё может `Wait(800)` на UI при рестарте STT | `HeadsetTestWindow.StopStt` |
| B2 | `RecognizeCompleted` → `BeginInvoke` → `RecognizeAsync` на UI | `StartSttFromPump` |
| B3 | Пока UI мёртв, пользователь жмёт Stop — **обработчик не входит**, в логе нет `StopMicAsync begin` | этот прогон |
| B4 | `VoiceInputService.StopAndTakeText` держит `lock` + `Monitor.Wait` + cancel | `VoiceInputService.cs` |
| B5 | Одновременно Play-STT + тест mic (раньше в логах) | двойной захват Hands-Free |

---

## 3. Карта методов (актуальная)

### Тест гарнитуры

| Метод | Файл | Статус |
|-------|------|--------|
| `StartMicAsync` | `HeadsetTestWindow.xaml.cs` | Pause A2DP, SCO render, WASAPI, STT |
| `StopMicAsync` | там же | SignalEnd → cancel/dispose в фоне |
| `OnCaptureData` | там же | peak/EQ + `WritePcm`; лог peak раз/сек |
| `StartSttFromPump` | там же | `SetInputToAudioStream` — **старт OK, событий нет** |
| `StopStt` | там же | `SignalEnd` + `Task.Wait(800)` — риск UI |

### Pump

| Метод | Файл | Статус |
|-------|------|--------|
| `WritePcm` / `Read` | `PcmPumpStream.cs` | Работает |
| `Length` / `Position` | там же | Виртуальные счётчики (фикс краша) |
| `SignalEnd` | там же | Разблок Read при стопе |
| `Read` → silence tick | там же | **Подозреваемый вредитель для SAPI** |

### Боевой голос / устройства

| Метод | Файл | Роль |
|-------|------|------|
| `VoiceInputService.Start` | `VoiceInputService.cs` | Default device SAPI (не pump) |
| `Cancel` / `StopCore_NoLock` | там же | Cancel + dispose в ThreadPool |
| `HeadsetAudioClaimer.PauseHoldForCapture` | `HeadsetAudioClaimer.cs` | Снять A2DP hold |
| `AudioPolicyConfig.TryClaimAllRoles` | `AudioPolicyConfig.cs` | Default endpoint `hr=0` |
| `MediaFocusClaimer` | `MediaFocusClaimer.cs` | BT Play → Tutor |

---

## 4. Что уже исправлено (и чего не хватает)

| Сделано | Не закрыто |
|---------|------------|
| `PcmPumpStream.Length` больше не кидает | SAPI не даёт hyp/rec при peak>0 |
| PolicyConfig vtable (`hr=0`) | Стабильный HFP при живом Stereo |
| Pause A2DP hold на время mic | UI freeze во время длинной сессии |
| `StopMicAsync` + SignalEnd | Нет таймаута/watchdog на «UI not pumping» |
| SMTC `add_ButtonPressed` | Боевой STT не на WASAPI-pump |

---

## 5. Рекомендуемый план (следующий проход)

1. **Убрать silence-padding** в `PcmPumpStream.Read`: при пустой очереди — коротко ждать и возвращать `0` или блок только на реальных PCM; не подмешивать нули.  
2. **Буфер → файл/MemoryStream сегментами** (1–2 с PCM) + `SetInputToWaveStream` / offline recognize — обход live-stream багов SAPI.  
3. **Не вызывать `Dispose`/долгий Wait на UI**; `StopStt` без `Wait`; кнопка Stop только `SignalEnd` + flag.  
4. **Recognizer culture = ru-RU** (если установлен) в тесте и в `VoiceInputService`.  
5. На время mic **не держать Stereo как default render** (в логе после SCO refresh снова `OUT=…Stereo`).  
6. Запретить параллельно Play-STT и HeadsetTest mic (частично есть `CancelVoiceIfListening`).  
7. Альтернатива SAPI: Windows.Media.SpeechRecognition / облачный STT, если HFP+SAPI так и не стабилизируется.

---

## 6. Ключевые цитаты лога

Сигнал был, распознавания не было:

```
2026-07-20 20:23:56.377 [INFO] HeadsetTest STT SetInputToAudioStream OK
2026-07-20 20:23:56.385 [INFO] HeadsetTest STT started en-US via pump
2026-07-20 20:23:59.233 [INFO] HeadsetTest peak=0.076 max=0.522 bytes=3200
2026-07-20 20:24:00.258 [INFO] HeadsetTest peak=0.155 max=0.522 bytes=3200
2026-07-20 20:24:01.282 [INFO] HeadsetTest peak=0.001 max=0.522 bytes=2560
… далее peak≈0 без STT hyp/rec …
```

Зависание UI: непрерывный `peak=` без строки `StopMicAsync begin` при попытке остановить.

---

## 7. Файлы

- `Hermes.EnglishTutorClient/Services/PcmPumpStream.cs`
- `Hermes.EnglishTutorClient/HeadsetTestWindow.xaml.cs`
- `Hermes.EnglishTutorClient/Services/VoiceInputService.cs`
- `Hermes.EnglishTutorClient/Services/HeadsetAudioClaimer.cs`
- `Hermes.EnglishTutorClient/Services/AudioPolicyConfig.cs`
- `Hermes.EnglishTutorClient/Services/MediaFocusClaimer.cs`

---

*Отчёт #2 по `app.log` сессии 20:23–20:25 и текущему коду `Hermes.EnglishTutorClient`.*
