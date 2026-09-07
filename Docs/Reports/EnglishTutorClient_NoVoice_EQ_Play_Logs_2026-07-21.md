# Отчёт #6: нет реакции на голос / EQ / Play — с выдержками лога

**Дата:** 2026-07-21 (~08:31–08:38)  
**Проект:** `Hermes.EnglishTutorClient`  
**Лог:** `%LOCALAPPDATA%\Hermes.EnglishTutorClient\logs\app.log`  
**Размер / mtime:** 22848 байт, 08:38:13  
**Сессия:** после `--- log cleared ---` в 08:31:25  

Жалобы пользователя ↔ лог:

| Жалоба | Подтверждение в логе |
|--------|----------------------|
| Тест: эквалайзер не реагирует | Захват **не стартует** → нет `peak=`, `peakMax=0.000` |
| Тест: «Старт mic» сбрасывается | После ERROR сразу `StopMicAsync` (кнопка возвращается в idle) |
| Главное: Play на гарнитуре молчит | **0** строк `SMTC button pressed` / `MediaPlay:` |
| Главное: 🎤 без реакции на голос | `Voice toggle: START` → `peak=0.000` → SAPI Internal error → STOP empty |

---

## Вердикт

1. **Тест гарнитуры сломан регрессом SCO hold:** после `SCO hold playback` → `WasapiCapture` падает **`0x88890008`** (×3 попытки). EQ нечему показывать.  
2. **Play на buds в этой сессии не доходит до приложения** (ни SMTC, ни hotkey).  
3. **Ручной 🎤** доходит до кода, но Hands-Free отдаёт **тишину** (`peak=0`), SAPI сразу Internal error, hyp/rec = 0.

---

## 1. Счётчики по сессии (после clear)

| Паттерн | Кол-во |
|---------|--------|
| `SMTC button pressed` | **0** |
| `MediaPlay:` (WM_HOTKEY / LL hook) | **0** |
| `Voice toggle` | **2** (START + STOP с UI 🎤) |
| `HeadsetTest capture:` ERROR `0x88890008` | **3** |
| `Internal error` (SAPI) | **1** |
| `Hypothesis` / `Recognized` | **0** |

---

## 2. Тест гарнитуры — почему сбрасывается «Старт mic»

Повторён **трижды** (08:33:16, 08:33:29, 08:33:40). Один и тот же сценарий:

```
MediaFocusClaimer: silent MediaPlayer paused for mic (A2DP release)
HeadsetAudioClaim hold paused for mic (release A2DP Stereo)
HeadsetTest capture start Headset (… Pixel Buds Pro 2 Hands-Free AG Audio)
PolicyConfig OK … Hands-Free …
HeadsetTest SCO render … Hands-Free …
HeadsetTest SCO hold playback on … Hands-Free …     ← near-silent WasapiOut на HF
RefreshDevices …
ERROR HeadsetTest capture: COMException (0x88890008)
   at IAudioClient.GetMixFormat
   at WasapiCapture..ctor(...)
   at HeadsetTestWindow.StartMicAsync … line 320
HeadsetTest StopMicAsync begin
HeadsetTest run peakMax=0.000
HeadsetTest StopMicAsync done
MediaFocusClaimer: silent MediaPlayer resumed after mic
HeadsetAudioClaim hold resumed on … Stereo …
```

### Полная выдержка первой попытки (08:33:16–08:33:22)

```
2026-07-21 08:33:16.845 [INFO] MediaFocusClaimer: silent MediaPlayer paused for mic (A2DP release)
2026-07-21 08:33:16.874 [INFO] HeadsetAudioClaim hold paused for mic (release A2DP Stereo)
2026-07-21 08:33:16.938 [INFO] HeadsetTest capture start Headset (#DebuggerPlus' Pixel Buds Pro 2 Hands-Free AG Audio)
2026-07-21 08:33:17.383 [INFO] PolicyConfig OK hr=0x00000000 role=Console → Hands-Free …
2026-07-21 08:33:17.450 [INFO] PolicyConfig OK hr=0x00000000 role=Multimedia → Hands-Free …
2026-07-21 08:33:17.513 [INFO] PolicyConfig OK hr=0x00000000 role=Communications → Hands-Free …
2026-07-21 08:33:17.638 [INFO] HeadsetTest SCO render Headset (… Hands-Free AG Audio)
2026-07-21 08:33:17.704 [INFO] HeadsetTest SCO hold playback on Headset (… Hands-Free AG Audio)
2026-07-21 08:33:19.678 [ERROR] HeadsetTest capture: System.Runtime.InteropServices.COMException (0x88890008):
   Exception from HRESULT: 0x88890008
   at NAudio.CoreAudioApi.Interfaces.IAudioClient.GetMixFormat(...)
   at NAudio.CoreAudioApi.WasapiCapture..ctor(...)
   at Hermes.EnglishTutorClient.HeadsetTestWindow.<StartMicAsync>d__32.MoveNext()
      ...HeadsetTestWindow.xaml.cs:line 320
2026-07-21 08:33:19.679 [INFO] HeadsetTest StopMicAsync begin
2026-07-21 08:33:19.689 [INFO] HeadsetTest run peakMax=0.000
2026-07-21 08:33:19.693 [INFO] HeadsetTest StopMicAsync done
```

**Почему UI «сбрасывает» кнопку:** в `catch` вызывается `StopMicAsync()` → `_micOn=false`, content снова «● Старт mic + STT». Захват не успевает вызвать `DataAvailable` → EQ = 0.

**Код HRESULT:** `0x88890008` = `AUDCLNT_E_UNSUPPORTED_FORMAT` (часто после смены BT-профиля / инвалидации endpoint; `GetMixFormat` на уже «битом» `MMDevice`).

**Причина по цепочке:** SCO hold (`WasapiOut` на Hands-Free render) + `RefreshDevices` **перед** `new WasapiCapture` → capture endpoint не открывается. Это регресс относительно сессий, где WASAPI хотя бы стартовал (иногда с `peak>0`).

---

## 3. Главное окно — Play на гарнитуре

За весь cleared-лог **нет**:

- `MediaFocusClaimer: SMTC button pressed: …`
- `MediaPlay: WM_HOTKEY …`
- `MediaPlay: LL hook VK_MEDIA_PLAY_PAUSE`

Есть только повторные `SMTC ButtonPressed subscribed via add_` после resume hold (подписка есть, **событий от buds нет**).

`Competing sessions check: manager null` — диагностика чужих медиасессий не сработала (API не отдал manager).

**Вывод:** AVRCP Play в этой сессии **не маршрутизируется** в EnglishTutorClient (или buds не шлют media key). Ручной 🎤 — единственный вход, который попал в лог.

---

## 4. Главное окно — ручной 🎤 без реакции на голос

```
2026-07-21 08:35:10.581 [INFO] Voice toggle: START live STT headset=DebuggerPlus' Pixel Buds Pro
2026-07-21 08:35:10.581 [INFO] MediaFocusClaimer: silent MediaPlayer paused for mic (A2DP release)
2026-07-21 08:35:10.610 [INFO] HeadsetAudioClaim hold paused for mic (release A2DP Stereo)
2026-07-21 08:35:10.611 [INFO] STT live Start cultureReq=ru-RU headset=DebuggerPlus' Pixel Buds Pro
2026-07-21 08:35:10.804 [INFO] STT capture devices: [Active] iVCam | [Active] HD Audio |
                              [Active] Headset (… Pixel Buds … Hands-Free …) | …
2026-07-21 08:35:11.189 [INFO] STT target mic: Headset (… Hands-Free …) state=Active
2026-07-21 08:35:11.192 [INFO] STT meter[pre-default] Mute=False vol=0.86 peak=0.000
2026-07-21 08:35:11.241 [INFO] PolicyConfig OK … → Hands-Free (Console/Multimedia/Communications)
2026-07-21 08:35:11.662 [INFO] STT default Communications='…Hands-Free…' Multimedia='…Hands-Free…'
2026-07-21 08:35:11.664 [INFO] STT meter[default-comm] Mute=False vol=0.86 peak=0.000
2026-07-21 08:35:11.665 [INFO] STT meter[default-mm] Mute=False vol=0.86 peak=0.000
2026-07-21 08:35:11.674 [ERROR] PickRecognizer: NO installed recognizer for cultureReq=ru-RU.
                              Installed: en-US. Falling back to en-US
2026-07-21 08:35:11.674 [WARN] STT: using en-US (ru-RU speech pack not installed). Speak English clearly.
2026-07-21 08:35:11.675 [INFO] STT recognizer=… (English - US) culture=en-US (cultureReq=ru-RU)
2026-07-21 08:35:11.780 [INFO] STT SetInputToDefaultAudioDevice OK
2026-07-21 08:35:11.781 [INFO] STT RecognizeAsync(Multiple) started
2026-07-21 08:35:11.881 [INFO] STT RecognizeCompleted cancelled=False
                              error=Internal error occurred in the recognition process. result=
2026-07-21 08:35:11.881 [WARN] STT RecognizeCompleted while listening - restarting RecognizeAsync
2026-07-21 08:35:11.886 [INFO] STT AudioState=Silence
2026-07-21 08:35:22.503 [INFO] Voice toggle: STOP live STT
2026-07-21 08:35:23.479 [INFO] STT Stop requested hyp=0 rec=0 rej=0 finalsLen=0 lastHypLen=0
2026-07-21 08:35:23.932 [WARN] STT Stop result empty
```

Интерпретация:

| Факт | Значение |
|------|----------|
| Кнопка 🎤 работает | `Voice toggle: START/STOP` есть |
| Hands-Free выбран | Active + PolicyConfig OK |
| Сигнал mic | **peak=0.000** до и после default |
| SAPI | падает через ~100 ms Internal error |
| Распознавание | hyp=0, rec=0, пустой STOP |
| Культура | только en-US (вторично; до текста дело не дошло) |

Пауза SMTC/A2DP вызывается, но **аудиопотока с mic нет** → UI «слушает», голос не обрабатывается.

---

## 5. Устройства (контекст)

Стабильно в сессии:

- **OUT:** `Headphones (#DebuggerPlus' Pixel Buds Pro Stereo)`  
- **IN (Active):** Hands-Free Pixel + iVCam + HD Audio mic  
- Батарея Pixel Buds: 100%  
- После ошибок теста hold снова на **Stereo** A2DP  

Конфликт Stereo (A2DP hold / SMTC) ↔ Hands-Free (mic) по-прежнему центральный.

---

## 6. Карта причин

| # | Приоритет | Причина | Доказательство |
|---|-----------|---------|----------------|
| 1 | **P0** | SCO hold перед `WasapiCapture` ломает endpoint (`0x88890008`) | 3× ERROR + мгновенный StopMic |
| 2 | **P0** | Hands-Free mic = тишина для SAPI (`peak=0` + Internal error) | блок 08:35:10–22 |
| 3 | **P0** | Play buds не генерирует SMTC/hotkey в этой сессии | 0 событий при живой подписке |
| 4 | P2 | Нет пакета речи ru-RU | `PickRecognizer` ERROR; на отказ mic не влияет |

---

## 7. Что делать дальше (для фикса, не сделано в этом отчёте)

1. **Убрать SCO hold до открытия capture** (или: open capture → при peak=0 короткий HF tone; всегда `GetDevice(id)` после PolicyConfig).  
2. Боевой STT: не live `SetInputToDefaultAudioDevice`, а **WASAPI Hands-Free → offline segments** (как работало при peak>0 раньше).  
3. Play: усилить удержание SMTC / проверить, уходит ли AVRCP в Chrome; UI 🎤 оставить как fallback (он уже логируется).  
4. Опционально: установить Windows Speech Pack **ru-RU**.

---

## 8. Одна фраза

После clear лога: **тест mic ×3 падает на `0x88890008` сразу после SCO hold (поэтому EQ=0 и кнопка сбрасывается); Play на buds не оставляет следов; ручной 🎤 стартует STT на Hands-Free с `peak=0` и SAPI Internal error → пустая запись.**
