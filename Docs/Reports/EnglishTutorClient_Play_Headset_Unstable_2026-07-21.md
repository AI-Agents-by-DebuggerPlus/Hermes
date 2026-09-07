# EnglishTutorClient — отчёт: нестабильный Play на гарнитуре

**Дата:** 2026-07-21  
**Лог:** `Hermes.EnglishTutorClient/logs/app.log` (сессия 11:58–12:02)  
**Устройство:** DebuggerPlus' Pixel Buds Pro (A2DP Stereo + Hands-Free HFP)

---

## Вердикт

Play с Pixel Buds приходит **только через SMTC AVRCP** (`Button = Pause`), **не** как клавиша `VK_MEDIA_PLAY_PAUSE`.

| Канал | Статус в логах |
|--------|----------------|
| SMTC `ButtonPressed` → `Pause` | Единственный рабочий путь |
| `RegisterHotKey` (PlayTest `0xE212`) | **FAIL win32=1409** (занят главным окном `0xE211`) |
| LL keyboard hook | **Ни разу** не сработал на Play гарнитуры |

Пока идёт **mic/STT (HFP/SCO)**, второго SMTC-события **нет** — стоп в главном окне был **без** `SMTC button pressed` (скорее UI / Cancel).

---

## Хронология логов (сжатая)

### A. Главное окно — старт Play OK, стоп без SMTC

```
11:58:52.676  MediaFocusClaimer: SMTC button pressed: Pause
11:58:52.689  MediaPlay: toggle voice listening=False
11:58:52.701  Voice toggle: START live STT
11:58:52.704  SMTC kept Playing during mic (MediaPlayer not paused)
11:58:52.715  hold paused for mic (release A2DP Stereo)
11:58:54.501  STT SCO WaveOut#0 wake @8000Hz
11:58:55.541  STT listening engine=Google
11:58:59–02  STT Google #1/#2 text=…
11:59:25.059  Voice toggle: STOP live STT     ← НЕТ строки SMTC button pressed!
```

**Вывод:** старт по AVRCP/SMTC; во время HFP-записи SMTC Play/Pause до приложения не дошёл.

### B. Play-тест #1 — 45 с, hits=0

```
11:59:40  Play-test started smtc=ok hotkey=no
11:59:40  PlayTest: RegisterHotKey failed win32=1409
12:00:25  Play-test stopped hits=0
```

**Вывод:** SMTC сессия теста поднята, но за ~45 с ни одного `ButtonPressed` (фокус/профиль BT / competing session).

### C. Play-тест #2 — 2 попадания, затем reclaim

```
12:01:28  Play-test started
12:01:30  PLAY HIT #1 source=SMTC armed=False
12:01:32  PLAY HIT #2 source=SMTC armed=True
12:01:39  Headset appeared — reclaimed audio/SMTC   ← MainWindow перехватывает defaults
12:02:07  Play-test stopped hits=2
```

**Вывод:** SMTC работает нестабильно; `Headset appeared → Reclaim` с главного окна может сбивать сессию теста.

### D. Дополнительно

```
12:00:57  Headset lost — waiting for reconnect
12:01:05  START live STT headset=(none)   ← имя гарнитуры уже потеряно
```

---

## Цепочка вызовов (коды)

### 1. SMTC → toggle (главное окно)

`MediaFocusClaimer.OnSmtcButtonPressed` → `PlayPauseFromSystem` → `MainWindow.HandleMediaPlayPause` → `ToggleVoiceInput`

```593:610:Hermes.EnglishTutorClient/Services/MediaFocusClaimer.cs
    private void OnSmtcButtonPressed(object sender, object args)
    {
        // ...
        AppLog.Info("MediaFocusClaimer: SMTC button pressed: " + (button?.ToString() ?? "?"));
        if (name.IndexOf("Play", ...) >= 0 || name.IndexOf("Pause", ...) >= 0)
        {
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                ClaimForeground();
                PlayPauseFromSystem?.Invoke();
            }));
        }
    }
```

```501:520:Hermes.EnglishTutorClient/MainWindow.xaml.cs
    private void HandleMediaPlayPause()
    {
        if (_playHandlingSuspended) return;
        if ((now - _lastPlayUtc).TotalMilliseconds < 120) return; // debounce 120ms
        ToggleVoiceInput();
    }
```

### 2. Старт записи (убивает A2DP, поднимает HFP)

`ToggleVoiceInput` → `PauseSilentPlayerForMic` + `PauseHoldForCapture` + `VoiceInputService.Start` (SCO wake + WaveIn 8 kHz)

```86:107:Hermes.EnglishTutorClient/Services/MediaFocusClaimer.cs
    public void PauseSilentPlayerForMic()
    {
        // MediaPlayer НЕ Pause — иначе AVRCP умирает
        // но A2DP hold всё равно снимается отдельно:
        SetTransportStatus(Playing);
    }
```

```104:108:Hermes.EnglishTutorClient/Services/HeadsetAudioClaimer.cs
    public void PauseHoldForCapture()
    {
        StopHold(); // release A2DP Stereo WasapiOut
    }
```

### 3. Play-тест (окно «Гарнитура»)

`StartPlayTest` → suspend MainWindow SMTC → свой `MediaFocusClaimer` + `MediaPlayHotkey(0xE212)` → `OnPlayHit`

```201:210:Hermes.EnglishTutorClient/HeadsetTestWindow.xaml.cs
    private void OnPlayHit(string source)
    {
        if ((now - _lastPlayHitUtc).TotalMilliseconds < 200) return; // debounce
        _playHitCount++;
        _playMicArmed = !_playMicArmed; // 🎤 ↔ ⏹
    }
```

`RegisterHotKey` для `0xE212` падает с **1409**, т.к. главное окно уже держит media key через `0xE211` / LL-hook; Pixel всё равно шлёт AVRCP, не HID-клавишу.

### 4. Конфликт reclaim при reconnect

```241:248:Hermes.EnglishTutorClient/MainWindow.xaml.cs
    // Headset appeared → _audioClaim.Reclaim()
    // даже когда открыт Play-тест (после suspend Main SMTC)
```

В логе 12:01:39 reclaim во время активного Play-теста.

---

## Корневые причины нестабильности

1. **AVRCP зависит от активной media-сессии (A2DP).** После `PauseHoldForCapture` + SCO/HFP Windows часто перестаёт слать `ButtonPressed` до возврата Stereo.  
2. **Нет запасного HID-канала** — LL/hotkey на Pixel Play не приходят.  
3. **Два владельца SMTC** (Main + PlayTest) + `RebuildSession` / `Reclaim` на reconnect гоняют сессию.  
4. **hotkey 1409** в тесте — ожидаемо; тест опирается только на SMTC.  
5. Debounce 120–200 ms не виноват в «молчании» — в логах нет `debounced` перед пропущенными нажатиями; события просто **не приходят**.

---

## Рекомендации (следующие шаги)

1. Пока `IsListening`: не отпускать A2DP hold *или* держать отдельный silent SMTC на **Speakers** (не BT), чтобы AVRCP оставался у приложения на время HFP-mic.  
2. Во время Play-теста: запретить MainWindow `Headset appeared → Reclaim`.  
3. Не полагаться на `RegisterHotKey` для Pixel — диагностический канал только SMTC.  
4. Логировать competing media session (если API доступен; сейчас D2 unpackaged → `RequestAsync=null`).

---

## Сводка по сессии

| Сценарий | SMTC hits | Итог |
|----------|-----------|------|
| Main: старт записи | 1 (Pause) | OK |
| Main: стоп/отправь | 0 | FAIL (стоп без SMTC) |
| Play-тест #1 (~45 с) | 0 | FAIL |
| Play-тест #2 | 2 | OK, затем reclaim |
