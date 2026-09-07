# Отчёт #3: English Tutor Client — полный hang UI после pcm_pump_fix_2

**Дата:** 2026-07-20 (~20:45–20:47)  
**Проект:** `Hermes.EnglishTutorClient`  
**Лог:** `%LOCALAPPDATA%\Hermes.EnglishTutorClient\logs\app.log`  
**Предыдущие:**  
- [`EnglishTutorClient_Mic_STT_Diagnosis_2026-07-20.md`](EnglishTutorClient_Mic_STT_Diagnosis_2026-07-20.md)  
- [`EnglishTutorClient_Mic_STT_Hang_2026-07-20.md`](EnglishTutorClient_Mic_STT_Hang_2026-07-20.md)

---

## Вердикт

После `pcm_pump_fix_2` (убрали silence-padding, non-blocking `StopStt`):

1. **UI снова полностью зависает** во время mic+STT.  
2. В логе **нет** `StopMicAsync begin` — клик «Стоп» **не доходит** до обработчика (диспетчер UI мёртв).  
3. Захват **живёт**: `peak=` пишется ~2 минуты с фонового потока.  
4. **SAPI по-прежнему молчит** (нет hyp/rec), хотя `max peak` до **0.465**.  
5. Процесс: высокий CPU (`CPU≈91` с старта ~20:41) при `Responding=True` (после/во время деградации).

`pcm_pump_fix_2` **не устранил hang** и **не дал распознавания**.

---

## 1. Хронология сессии (log cleared 20:45:27)

| Время | Событие | Интерпретация |
|-------|---------|---------------|
| 20:45:33 | OUT=Stereo, IN=Hands-Free Active; hold paused | Старт теста |
| 20:45:34 | SCO + PolicyConfig `hr=0` | Defaults OK |
| 20:45:35 | WASAPI 16 kHz float | Capture OK |
| **20:45:36** | **`StopStt begin (non-blocking)`** | Это **не** стоп пользователя — вызов из `StartSttFromPump` → `StopStt()` перед новым engine |
| 20:45:36 | `SetInputToAudioStream OK`, STT started en-US | Старт SAPI OK |
| 20:45:38–42 | `peak` до **0.442** | Реальный сигнал был |
| 20:46:22 | `peak=0.285`, max **0.465** | Ещё один всплеск |
| 20:45:36–20:47:30+ | `peak=` каждую секунду | Capture thread работает |
| — | **Нет** `StopMicAsync begin` | UI не обрабатывает клик |
| — | **Нет** STT hyp/rec/rejected | Распознавание мёртво |

---

## 2. Почему «всё виснет» (уточнение)

### Паттерн (тот же, что в отчёте #2)

```
UI thread:  заблокирован / не качает очередь сообщений
Capture:    OnCaptureData → AppLog peak= …   ← продолжает писать
Кнопка Stop: не вызывает StopMicAsync        ← нет строки в логе
```

### Что уже пробовали и результат

| Фикс | Ожидание | Факт в этой сессии |
|------|----------|-------------------|
| `Length`/`Position` counters | Убрать crash SpStreamWrapper | OK, старт проходит |
| `SignalEnd` + async dispose | Убрать deadlock на Stop | **Stop не вызывается** — UI мёртв раньше |
| Убрать silence padding | Дать SAPI чистый PCM | hyp/rec всё равно нет |
| `StopStt` без `Wait(800)` | Не блокировать UI на стопе | Бесполезно, если UI уже завис на listen |

### Наиболее вероятные причины зависания UI *во время* listen

| # | Гипотеза | Почему |
|---|----------|--------|
| **H1** | `DispatcherTimer` (50 ms) → `UpdateMetersUi` → `AudioMeterInformation.MasterPeakValue` / endpoint COM на Hands-Free | COM к BT-устройству на **UI-потоке** часто «клеит» WPF |
| **H2** | `Dispatcher.BeginInvoke(AppendStt)` на каждый peak + раздувание `SttLogBox` | Очередь диспетчера + тяжёлый AppendText |
| **H3** | SAPI + `PcmPumpStream.Read` (busy Wait/Reset) грузит CPU → UI голодает | CPU процесса высокий |
| **H4** | Sync-переход в SAPI/NAudio с UI в `StartSttFromPump` / refresh devices | `RefreshDevices` + enumeration на UI после SCO |

`StopMicAsync` / non-blocking `StopStt` **не лечат H1–H3**, пока зависание происходит **до** клика Stop.

---

## 3. Почему нет реакции на голос

| Наблюдение | Вывод |
|------------|--------|
| `SetInputToAudioStream OK` | Pump совместим с конструктором SAPI |
| `peak max=0.465`, bytes>0 | WASAPI → WritePcm доставляет речь |
| Нет hyp/rec/rejected | SAPI dictation **не принимает** поток как речь |
| Recognizer **en-US**, речь часто ru | Отдельный фактор (TODO в коде уже стоит) |
| После всплеска peak снова ~0 | HFP нестабилен / Stereo снова default OUT |

Вероятно **связка**: нестабильный Hands-Free + live stream в System.Speech плохо подходит для Pixel Buds; убрать silence помогло «честности» потока, но не заставило SAPI распознать.

---

## 4. Карта методов (фокус hang)

| Метод | Файл | Роль в hang |
|-------|------|-------------|
| `UpdateMetersUi` | `HeadsetTestWindow.xaml.cs` | **Подозреваемый** — COM meter на UI 20 Hz |
| `OnCaptureData` | там же | peak log + `BeginInvoke(AppendStt)` |
| `StartSttFromPump` | там же | Sync на UI; зовёт `StopStt` (лог 20:45:36) |
| `StopMicAsync` | там же | **Не вызывается** при зависании |
| `StopStt` | там же | Non-blocking OK; не спасает listen-hang |
| `PcmPumpStream.Read` | `PcmPumpStream.cs` | Wait без silence; возможен CPU spin |
| `SignalEnd` | там же | Не достигнуто при UI hang |

---

## 5. Рекомендации (следующий проход) — приоритет

1. **Снять COM/meter с UI-потока**  
   - `UpdateMetersUi`: не трогать `AudioMeterInformation` на UI; peak только из `_inPeak` (уже считается в `OnCaptureData`).  
   - Или вынести timer callback в `Task` + `BeginInvoke` только для присвоения `ProgressBar.Value`.

2. **Не логировать peak в UI TextBox каждую секунду**  
   - Только `AppLog`; в окно — раз в 5 с или по кнопке.

3. **Отказаться от live SAPI+pump для BT**  
   - Копить 1–2 с PCM → временный WAV → `SetInputToWaveFile` / offline `Recognize` — обход live-stream.  
   - Либо другой движок (Windows.Media.SpeechRecognition / облако).

4. **Watchdog**  
   - Если UI не отвечает N секунд при `_micOn` — `SignalEnd` + stop capture с фонового таймера (без UI).

5. **ru-RU recognizer** (отдельный TODO) — после стабилизации UI/потока.

6. Не считать успехом только `StopStt begin` в начале `StartSttFromPump` — для стопа пользователя обязательна строка **`StopMicAsync begin`**.

---

## 6. Ключевые цитаты лога

```
2026-07-20 20:45:36.002 [INFO] HeadsetTest StopStt begin (non-blocking)   ← старт, не стоп юзера
2026-07-20 20:45:36.268 [INFO] HeadsetTest STT started en-US via pump
2026-07-20 20:45:42.099 [INFO] HeadsetTest peak=0.008 max=0.442 bytes=1280
2026-07-20 20:46:22.633 [INFO] HeadsetTest peak=0.285 max=0.442 bytes=1920
… peak= продолжается …
(нет StopMicAsync begin)
(нет STT hyp / rec)
```

---

## 7. Итог по трём отчётам

| # | Симптом | Корневой слой |
|---|---------|---------------|
| 1 | Crash `Length` | `PcmPumpStream` — **исправлено** |
| 2 | peak>0, нет STT; hang на Stop | Silence padding + Dispose на UI — **частично** |
| **3** | Hang на listen; Stop не входит; STT молчит | **UI thread blocked (скорее COM/dispatcher); SAPI live непригоден** |

---

*Отчёт #3 по сессии 20:45–20:47 после применения `pcm_pump_fix_2.zip`.*
