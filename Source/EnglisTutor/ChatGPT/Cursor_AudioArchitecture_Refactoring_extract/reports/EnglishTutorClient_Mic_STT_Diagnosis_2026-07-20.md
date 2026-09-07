# Отчёт: микрофон / STT — English Tutor Client

**Дата:** 2026-07-20  
**Проект:** `Hermes.EnglishTutorClient`  
**Лог:** `%LOCALAPPDATA%\Hermes.EnglishTutorClient\logs\app.log`  
**Вердикт:** тест гарнитуры падает на старте STT из‑за несовместимости `PcmPumpStream` с SAPI; ранее распознавание также не работало из‑за тишины Hands-Free / конфликта устройств / сломанного PolicyConfig.

---

## 1. Что видно в логах (последний прогон ~17:57)

### 1.1 Устройства — ок

```
OUT = Headphones (#DebuggerPlus' Pixel Buds Pro Stereo)
IN  = Headset (#DebuggerPlus' Pixel Buds Pro 2 Hands-Free AG Audio)  state=Active
PolicyConfig OK hr=0x00000000  (Console / Multimedia / Communications)
HeadsetTest WASAPI 32 bit IEEEFloat: 16000Hz 1 channels
```

Захват WASAPI стартует. Default Communications успешно ставится на Hands-Free.

### 1.2 Распознавание — жёсткий сбой (корневая причина текущего «тест не работает»)

```
[ERROR] HeadsetTest STT: System.NotSupportedException: Specified method is not supported.
   at PcmPumpStream.get_Length()
   at System.Speech.Internal.SapiInterop.SpStreamWrapper..ctor(Stream stream)
   at SpeechRecognitionEngine / RecognizerBase.SetInput(Stream, SpeechAudioFormatInfo)
   at HeadsetTestWindow.StartSttFromPump(...)
```

Повторено дважды (17:57:26 и 17:57:47).  
**Следствие:** `RecognizeAsync` даже не запускается → гипотез/финалов нет → в UI «голос не распознаётся».

SAPI при `SetInputToAudioStream` в конструкторе `SpStreamWrapper` **читает `Stream.Length`**.  
`PcmPumpStream.Length` бросает `NotSupportedException` → весь пайплайн STT в окне теста мёртв.

### 1.3 Исторический прогон Play (16:45) — другая, более глубокая проблема

```
STT meter peak=0.000
STT AudioState=Silence → Stopped
RecognizeCompleted result=
Stop hyp=0 rec=0
PolicyConfig hr=0x800706F4  (тогда vtable IPolicyConfig был неверный)
```

Даже при Active Hands-Free **уровень сигнала = 0** — Windows/BT не отдавал аудио в SAPI.  
Сейчас PolicyConfig исправлен (`hr=0`), но тест всё равно ломается на `Length` до проверки реального сигнала.

### 1.4 Долгие периоды «Active audio endpoint: (none found)»

BT-батарея читается (Pixel Buds ~47–60%), но default **render** часто отсутствует → профиль A2DP Stereo не поднят. Hands-Free при этом может быть Unplugged → mic недоступен для capture.

---

## 2. Карта методов (устройства + голос)

### 2.1 Окно теста гарнитуры

| Метод | Файл | Роль |
|-------|------|------|
| `RefreshDevices` | `HeadsetTestWindow.xaml.cs` | Выбор OUT (Stereo) / IN (Hands-Free) через NAudio `MMDeviceEnumerator` |
| `PreferHeadsetRender` / `PreferHandsFree` | там же | Фильтр Pixel / Headphones / Hands-Free, без iVCam |
| `PlayTone_OnClick` / `StopTone` | там же | WasapiOut + `SignalGenerator` 440 Hz; toggle стоп |
| `StartMic` | там же | `AudioPolicyConfig.TryClaimAllRoles` → `WasapiCapture.StartRecording` → `StartSttFromPump` |
| `OnCaptureData` | там же | Peak/EQ + `ToPcm16Mono` → `PcmPumpStream.WritePcm` |
| **`StartSttFromPump`** | там же (~стр. 320–400) | **SAPI** `SetInputToAudioStream(_pcmPump, …)` → **падает** |
| `StopMic` / `StopStt` | там же | Остановка capture + dispose engine |

### 2.2 Поток PCM для SAPI (баг)

| Метод / свойство | Файл | Роль |
|------------------|------|------|
| `PcmPumpStream.WritePcm` / `Read` | `Services/PcmPumpStream.cs` | Producer (WASAPI) / consumer (SAPI) |
| **`get_Length`** | стр. 22 | **`throw NotSupportedException`** — несовместимо с SAPI |
| `CanSeek` | стр. 20 | `false` — для live-stream ок, но Length всё равно нужен SpStreamWrapper |

### 2.3 Боевой голосовой ввод (Play / кнопка 🎤)

| Метод | Файл | Роль |
|-------|------|------|
| `MainWindow.HandleMediaPlayPause` | `MainWindow.xaml.cs` | Toggle listening по Play |
| `MainWindow.ToggleVoiceInput` | там же | Start/Stop `_voice` + SMTC status |
| **`VoiceInputService.Start`** | `Services/VoiceInputService.cs` | Pick Hands-Free → PolicyConfig → `SpeechRecognitionEngine` → **`SetInputToDefaultAudioDevice`** (не pump) |
| `PickHandsFree` | там же | Выбор Hands-Free по имени гарнитуры |
| `OnHypothesized` / `OnRecognized` / `OnRejected` | там же | События SAPI → UI / лог |
| `OnRecognizeCompleted` | там же | Рестарт `RecognizeAsync` при неожиданном Completed |
| `StopAndTakeText` | там же | Сбор finals + last hypothesis |

**Важно:** боевой путь **не** использует `PcmPumpStream`. Он слушает **Windows default capture**. Если default = iVCam или Hands-Free молчит (`peak=0`), STT пустой даже без исключения Length.

### 2.4 Перехват устройств / медиасессии

| Метод | Файл | Роль |
|-------|------|------|
| `HeadsetAudioClaimer.Claim` / `Reclaim` | `Services/HeadsetAudioClaimer.cs` | Default OUT/IN + тихий WasapiOut «hold» |
| `PreferHeadsetRender` / `PreferHandsFree` | там же | Только **Active** Hands-Free (Unplugged не ставим default) |
| `StartHoldPlayback` | там же | Near-silent sine на render endpoint |
| `AudioPolicyConfig.TrySetDefaultEndpoint` | `Services/AudioPolicyConfig.cs` | COM `IPolicyConfig.SetDefaultEndpoint` |
| `TryClaimAllRoles` | там же | Console + Multimedia + Communications |
| `TryPolicyConfig` / `TryPolicyConfigVista` | там же | Два CLSID; раньше vtable давал `0x800706F4` |
| `MediaFocusClaimer.Start` / `TryEnableSmtc` | `Services/MediaFocusClaimer.cs` | SMTC + silent MediaPlayer → BT Play |
| `TrySubscribeButtonPressed` | там же | `add_ButtonPressed` (не `EventInfo.AddEventHandler`) |
| `MediaPlayHotkey` | `Services/MediaPlayHotkey.cs` | `RegisterHotKey` + `WH_KEYBOARD_LL` для VK_MEDIA_PLAY_PAUSE |

### 2.5 Цепочка вызовов (тест mic + STT) — где обрыв

```
MicToggle_OnClick
  → StartMic
       → RefreshDevices / PreferHandsFree
       → AudioPolicyConfig.TryClaimAllRoles          ✅ hr=0
       → WasapiCapture.StartRecording                ✅ 16 kHz float
       → StartSttFromPump
            → new SpeechRecognitionEngine
            → LoadGrammar(DictationGrammar)
            → SetInputToAudioStream(_pcmPump, fmt)   ❌ NotSupportedException (Length)
            → RecognizeAsync                         (не доходит)
```

---

## 3. Сводка проблем (по слоям)

| # | Слой | Симптом в логе | Причина | Где код |
|---|------|----------------|---------|---------|
| **A** | Тест STT (сейчас) | `NotSupportedException` на `get_Length` | SAPI требует `Stream.Length`; pump кидает | `PcmPumpStream.get_Length`, `StartSttFromPump` |
| **B** | Сигнал mic | `peak=0.000`, `AudioState=Silence` | Hands-Free Active, но аудио не идёт (A2DP Stereo без HFP / тихий endpoint) | meter в `VoiceInputService.LogMeter`, EQ в `OnCaptureData` |
| **C** | Выбор mic | Communications = iVCam | Default не Hands-Free; PolicyConfig раньше ломался (`0x800706F4`) | `AudioPolicyConfig`, `HeadsetAudioClaimer.Claim` |
| **D** | BT профиль | `Active audio endpoint: (none found)`, Hands-Free `Unplugged` | Нет активного Stereo/HFP — mic физически недоступен | `DefaultAudioDeviceReader`, `PreferHandsFree` |
| **E** | Конфликт capture | WASAPI держит Hands-Free, SAPI на default | Dual open / wrong default (до pump) | старый `StartStt` + `SetInputToDefaultAudioDevice` |
| **F** | Play → Gemini | SMTC fail (исторически) | WinRT `AddEventHandler` | исправлено через `add_ButtonPressed` |

---

## 4. Рекомендуемые исправления (по приоритету)

1. **`PcmPumpStream`:** реализовать `Length` как «очень большое» значение (или `long.MaxValue` / нарастающий счётчик байт), `CanSeek=false` оставить; либо писать WAV во временный файл / `MemoryStream` сегментами — SAPI должен пройти `SpStreamWrapper` ctor.
2. После фикса Length — в тесте смотреть **peak EQ**: если peak>0 и STT всё ещё пуст → проблема формата/культуры recognizer; если peak=0 → проблема BT HFP (форсировать Hands-Free: звонок/открытие capture exclusive, отключить A2DP hold conflict).
3. **Боевой `VoiceInputService`:** перейти на тот же WASAPI→PCM→SAPI путь после фикса pump (не полагаться на default + iVCam).
4. Не ставить default на Unplugged Hands-Free; reclaim только когда Stereo Active (уже частично сделано в `PreferHandsFree` Active-only).
5. Проверить, не глушит ли `HeadsetAudioClaimer.StartHoldPlayback` (A2DP Stereo) профиль Hands-Free на Pixel Buds — при необходимости паузить hold на время mic.

---

## 5. Ключевые цитаты из лога

**Текущий блокер теста:**
```
2026-07-20 17:57:26.421 [INFO] HeadsetTest WASAPI 32 bit IEEFloat: 16000Hz 1 channels
2026-07-20 17:57:26.798 [ERROR] HeadsetTest STT: System.NotSupportedException
   at PcmPumpStream.get_Length() ... PcmPumpStream.cs:line 22
   at HeadsetTestWindow.StartSttFromPump ... HeadsetTestWindow.xaml.cs:line 353
```

**Ранний Play (тишина):**
```
2026-07-20 16:45:33.198 [INFO] STT meter[pre-default] ... peak=0.000
2026-07-20 16:45:34.240 [INFO] STT AudioState=Silence
2026-07-20 16:45:46.976 [INFO] STT Stop requested hyp=0 rec=0 ...
```

---

## 6. Файлы для правок

- `Hermes.EnglishTutorClient/Services/PcmPumpStream.cs` — **критично**
- `Hermes.EnglishTutorClient/HeadsetTestWindow.xaml.cs` — `StartSttFromPump`, `OnCaptureData`
- `Hermes.EnglishTutorClient/Services/VoiceInputService.cs` — боевой STT
- `Hermes.EnglishTutorClient/Services/AudioPolicyConfig.cs` — defaults
- `Hermes.EnglishTutorClient/Services/HeadsetAudioClaimer.cs` — hold / reclaim

---

*Отчёт сформирован по `app.log` и исходникам `Hermes.EnglishTutorClient` на 2026-07-20.*
