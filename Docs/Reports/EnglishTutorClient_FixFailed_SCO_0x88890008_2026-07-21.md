# Отчёт #5: English Tutor Client — фикс не сработал (логи 08:18–08:24)

**Дата:** 2026-07-21  
**Проект:** `Hermes.EnglishTutorClient`  
**Лог:** `%LOCALAPPDATA%\Hermes.EnglishTutorClient\logs\app.log`  
**Сессия после перезапуска с паузой SMTC / SCO hold:** ~08:18:28 → 08:24:36  
**Предыдущие:** Diagnosis / Hang / UI_Hang / STT_Garbage_PlayDead / MicSilent_SttGarbage_Play  

---

## Вердикт

| Вопрос | Ответ по логу |
|--------|----------------|
| Play стартует запись? | **Да, один раз** — `SMTC button: Pause` → `Voice toggle: START` (08:23:07) |
| Главное окно слышит голос / даёт текст? | **Нет** — `peak=0.000`, затем SAPI **Internal error**, рестарт падает |
| Повторный Play (стоп → чат)? | **Нет следов** — после старта нет второго SMTC / MediaPlay |
| Тест гарнитуры mic+STT? | **Сломан жёстче** — `WasapiCapture` падает `0x88890008`, `peakMax=0.000` |
| ru-RU recognizer? | **Нет** — установлен только `en-US` |

**Итог:** последние правки (pause SMTC MediaPlayer + SCO hold на Hands-Free) **не восстановили mic**. В тесте гарнитуры они, судя по логу, **ухудшили** ситуацию: захват больше не стартует.

---

## 1. Хронология (новая сессия)

| Время | Событие |
|-------|---------|
| 08:18:28 | Старт приложения: SMTC OK, hotkey OK |
| 08:18:29 | BT ещё не Active → hold на **Speakers**, Communications = **iVCam** |
| 08:18:30 | `InstalledRecognizers (1): en-US` only |
| 08:16–08:17 *(до рестарта в старом процессе)* | `Active audio endpoint: (none found)` / Headset lost |
| **08:23:07** | **Play на buds** → SMTC `Pause` → START live STT, `headset=(none)` |
| 08:23:08 | `silent MediaPlayer paused` + A2DP hold paused — фикс сработал по коду |
| 08:23:10–11 | Target mic = Pixel Hands-Free Active, но **meter peak=0.000** |
| 08:23:11 | Fallback en-US (ru-RU нет) |
| 08:23:12–14 | `RecognizeCompleted` **Internal error** → restart → **No audio input** |
| 08:23:39 | Открыт тест гарнитуры (live STT ещё «слушал» до Cancel) |
| 08:23:44 | Cancel main STT; SCO hold playback ON Hands-Free |
| **08:23:47** | **`WasapiCapture` COMException `0x88890008`** → mic не стартовал |
| 08:23:48–53 | Resume A2DP Stereo + reclaim SMTC; позже `Headset appeared — reclaimed` |
| 08:24:11 | Закрытие окна теста; **нет** нового Play в логе |

---

## 2. Главное окно — голос / Play

### Что работает
- SMTC подписка жива: первый Play доходит (`SMTC button pressed: Pause`).
- Пауза silent MediaPlayer пишется в лог.

### Что ломается

```
STT meter[…] peak=0.000
STT RecognizeCompleted … Internal error occurred in the recognition process
STT restart after Completed failed: No audio input is supplied to this recognizer
```

Цепочка:
1. Hands-Free выбран и выставлен default Communications/Multimedia (`PolicyConfig OK`).
2. Уровень с микрофона **нулевой** уже до `RecognizeAsync`.
3. SAPI падает с internal error; повторный `RecognizeAsync` без повторного `SetInputToDefaultAudioDevice` → «No audio input».
4. Hypotheses / Recognized / Supabase send — **отсутствуют**.
5. Второго Play (стоп/отправка) в логе **нет** — либо buds не шлют AVRCP после сломанной сессии, либо пользователь ушёл в тест гарнитуры.

Дополнительно: при старте STT `headset=(none)` — `_activeHeadsetName` пустой (после `(none found)` / до reclaim). Mic всё равно выбирается по Hands-Free в списке устройств, но имя гарнитуры для скоринга не используется.

---

## 3. Тест гарнитуры — регресс

Порядок в `StartMicAsync` (по логу):

1. Pause SMTC + pause A2DP hold  
2. PolicyConfig → Hands-Free  
3. **`SCO hold playback`** на Hands-Free render (новый шаг)  
4. `RefreshDevices`  
5. `new WasapiCapture(...)` → **ERROR**

```
HeadsetTest capture: COMException (0x88890008)
  at IAudioClient.GetMixFormat
  at WasapiCapture..ctor(...)
  HeadsetTestWindow.StartMicAsync line ~320
peakMax(run)=0.000
```

`0x88890008` = **AUDCLNT_E_UNSUPPORTED_FORMAT** (часто после смены профиля BT / инвалидации endpoint: устройство пересоздано, старый `MMDevice` уже невалиден, либо mix format недоступен в момент открытия).

Ранее (до SCO hold) захват хотя бы **открывался** (иногда с `peak>0`, иногда с `peak=0`). Сейчас — **не открывается вообще**.

После ошибки сразу Resume A2DP Stereo — профиль снова Stereo, mic-сессия оборвана.

---

## 4. Культура / качество текста

Без изменений относительно прошлого отчёта:

```
InstalledRecognizers (1): en-US […]
PickRecognizer: NO installed recognizer for cultureReq=ru-RU … Falling back to en-US
```

В этой сессии «левый текст» даже не проявился: **распознавания не было** (SAPI умер на input). Проблема культуры остаётся блокирующей для русского, но **не является** причиной текущего полного отказа mic.

---

## 5. Карта методов → статус по этой сессии

| Область | Метод | Статус |
|---------|--------|--------|
| Play in | `MediaFocusClaimer.OnSmtcButtonPressed` | OK (1× Pause) |
| A2DP release | `PauseSilentPlayerForMic` + `PauseHoldForCapture` | Вызваны; mic всё равно peak=0 |
| Live STT | `VoiceInputService.Start` | Default HF выставлен; peak=0; Internal error |
| Live STT restart | `OnRecognizeCompleted` | Баг: restart без `SetInput…` → No audio input |
| Offline test | `StartMicAsync` + `StartScoHold` | **Регресс:** capture ctor fail `0x88890008` |
| Play out / send | `HandleMediaPlayPause` stop path | Не достигнуто (нет 2-го Play, нет текста) |
| Culture | `SpeechRecognizerPicker` | Только en-US |

---

## 6. Корневые причины (ранжирование)

1. **P0 — Hands-Free mic без сигнала / нестабильный endpoint**  
   Даже при Active + PolicyConfig `peak=0`; после SCO hold endpoint ломается для WASAPI (`0x88890008`). Типичный BT конфликт Stereo↔SCO / гонка после `RefreshDevices`.

2. **P0 — SCO hold сделал тест хуже**  
   Near-silent playback на HF render перед `WasapiCapture` совпадает с падением GetMixFormat. Нужен другой порядок: capture first **или** re-resolve device by ID после SCO, без устаревшего `MMDevice`.

3. **P1 — SAPI restart без SetInput**  
   После Internal error движок без входа; UI «слушает», но STT мёртв до Cancel.

4. **P1 — повторный Play не подтверждён**  
   После сломанного listen / теста нет SMTC-событий; отдельная проверка после стабилизации mic.

5. **P2 — нет ru-RU speech pack**  
   Качество русского текста невозможно на SAPI до установки пакета; для EN-уроков нужен стабильный en-US + нормальный PCM.

---

## 7. Рекомендации (следующий проход)

### A. Тест гарнитуры (снять регресс)
1. Убрать или отложить SCO hold: сначала `WasapiCapture.StartRecording`, при `peak≈0` — тогда короткий HF beep/hold.  
2. После любого PolicyConfig / HF playback — **`GetDevice(id)` заново** перед `WasapiCapture`.  
3. Логировать `DataAvailable` count / bytes даже при peak=0 (отличить «нет callback» vs «тишина»).

### B. Live STT
1. При `RecognizeCompleted` + Internal error / No audio input — полный **пересоздание engine** + `SetInputToDefaultAudioDevice`, не голый `RecognizeAsync`.  
2. Перед Start: пауза SMTC + A2DP, **короткая HF playback**, wait, re-claim capture default, meter; стартовать SAPI только если peak/callback жив **или** сразу идти в offline WASAPI→segment path (как в тесте, когда capture работал).  
3. Перенести боевой путь на **offline segmented** с Hands-Free WASAPI (live default device на BT нестабилен).

### C. Play / отправка
1. После успешного mic-stop — `ReassertPlayingStatus` (уже есть); добавить UI-статус «Жду Play…» и лог heartbeat SMTC.  
2. Не полагаться только на AVRCP: кнопка ⏹ в UI должна оставаться надёжным стопом.

### D. Система
1. Установить Windows speech pack **ru-RU**, если нужен русский.  
2. На время теста закрыть Chrome/Gemini media (Competing sessions API здесь `manager null` — диагностику усилить).

---

## 8. Вывод одной фразой

Фикс «pause SMTC + SCO hold» подтверждён в логе вызовами, но **mic по-прежнему peak=0 / SAPI Internal error**, а тест гарнитуры теперь **падает на `WasapiCapture` (`0x88890008`)** — нужен откат/перестановка SCO hold и устойчивый WASAPI→offline STT вместо live default device.
