# Отчёт #4: English Tutor Client — STT отвечает, текст «левый», Play на гарнитуре мёртв

**Дата:** 2026-07-20 (~21:03–21:06)  
**Проект:** `Hermes.EnglishTutorClient`  
**Лог:** `%LOCALAPPDATA%\Hermes.EnglishTutorClient\logs\app.log`  
**Предыдущие:** Diagnosis / Hang / UI_Hang_After_Fix2  

---

## Вердикт

| Вопрос | Ответ по логу |
|--------|----------------|
| Реакция на голос в **тесте гарнитуры** | **Да** — `STT recognized: …`, peak до **0.764**, Stop без зависания |
| Реакция на голос в **главном окне** | **Да** — `AudioState=Speech`, hyp/rec, stop → текст ушёл в Supabase |
| Текст соответствует речи | **Нет** — en-US dictation выдаёт бессмыслицу при `cultureReq=ru-RU` |
| Play на гарнитуре | **Нет следов** — ни SMTC button, ни `MediaPlay: LL hook` / `WM_HOTKEY` |

Fix #3 (offline STT + UI без COM meter) **разблокировал захват и стоп**. Качество распознавания и BT Play — следующие проблемы.

---

## 1. Тест гарнитуры (offline STT) — 21:03:36–21:04:34

**Хорошо:**
- WASAPI Hands-Free, offline recognizer стартует  
- `peak` до 0.764  
- `StopMicAsync begin` / `done` — UI не завис  
- Сегменты реально проходят в SAPI  

**Плохо — «левый» текст (en-US на не-английскую/шумную речь):**

```
recognized: Law
recognized: Eat
recognized: CD
recognized: So little seen
recognized: The suit the
recognized: Osuna reducing the
recognized: Dicks to test
recognized: The east coast
recognized: Galoob Wilson
recognized: Voice message was
…
segment rejected: (no confident match)
```

Причины:
1. `OfflineSttRecognizer Start culture=en-US` (жёстко; TODO ru-RU не сделан).  
2. Короткие сегменты 1.5 с + HFP 16 kHz — SAPI «угадывает» английские слова.  
3. Нет порога confidence — любой `Recognize()` с текстом принимается.

---

## 2. Главное окно (live `VoiceInputService`) — 21:05:32–21:06:16

```
Voice toggle: START … cultureReq=ru-RU
STT recognizer=… (English - US) culture=en-US    ← запрос ru, движок en
AudioState=Speech
Hypothesis … team that law …
Recognized #1 conf=0.22  Team that law that he should season one coup for the full five
Recognized #2 conf=0.42  long cool for the full five
AudioSignalProblem=TooSlow
Stop result → Supabase sent chars=90
```

Тот же паттерн: **голос слышен**, **текст мусор**, confidence низкий (0.22–0.42).  
`cultureReq=ru-RU` **игнорируется** — `PickRecognizer` падает на en-US.

Старт/стоп в этом фрагменте идут как `Voice toggle:` **без** строк `MediaPlay:` / `SMTC button` → пользователь, скорее всего, жал **кнопку 🎤 в UI**, не Play наbuds.

---

## 3. Play на гарнитуре — нет событий

После `log cleared` (21:03:34) до конца сессии **отсутствуют**:

- `Media focus SMTC button: Play/Pause`  
- `MediaPlay: LL hook VK_MEDIA_PLAY_PAUSE`  
- `MediaPlay: WM_HOTKEY`  

Ранее (другие сессии) SMTC иногда срабатывал (`SMTC button: Pause`). Сейчас AVRCP/Play **не доходит** до Tutor.

**Вероятные причины:**

| # | Гипотеза | Детали |
|---|----------|--------|
| P1 | Активная медиасессия у **Chrome/Gemini** | BT Play уходит туда, не в наш muted MediaPlayer |
| P2 | `HeadsetAudioClaimer` hold на **Stereo** | После теста hold resume на A2DP; SMTC-сессия Tutor слабее |
| P3 | Play = только AVRCP → SMTC | LL hook / RegisterHotKey ловят лишь клавиатурный VK; buds часто не шлют key |
| P4 | SMTC `PlaybackStatus` / silent loop | Сессия не «Playing» в момент нажатия или ButtonPressed не подписан после reclaim |

Пока Play не пишет в лог — чинить нужно **цепочку MediaFocusClaimer + удержание SMTC**, не STT.

---

## 4. Карта методов (актуальная)

| Область | Метод / класс | Статус по этой сессии |
|---------|---------------|------------------------|
| Тест STT | `OfflineSttRecognizer` | Работает, текст плохой |
| Тест stop | `StopMicAsync` | OK, hang снят |
| UI meters | `UpdateMetersUi` без COM | OK (нет hang) |
| Главный STT | `VoiceInputService.Start` + default device | Слышит, en-US, мусор |
| Культура | `cultureReq=ru-RU` → engine en-US | **Баг выбора recognizer** |
| Play | `MediaFocusClaimer` / `MediaPlayHotkey` | **Нет событий в логе** |

---

## 5. Рекомендации (следующий проход)

### A. Текст ↔ речь
1. Ставить **ru-RU** recognizer, если установлен (`InstalledRecognizers`), иначе явно логировать fallback.  
2. Offline: порог `result.Confidence` (например ≥ 0.5), иначе `segment rejected`.  
3. Увеличить сегмент (2–3 с) или VAD по peak перед flush.  
4. Перенести offline-путь и в `VoiceInputService` (live default device даёт TooSlow + мусор).

### B. Play на гарнитуре
1. При старте и после reclaim: `SetTransportStatus(Playing)` + проверить подписку SMTC в логе.  
2. Логировать каждый AVRCP/SMTC/hotkey attempt; при отсутствии — предупреждение «Play не наш».  
3. Не отдавать медиафокус Chrome: при Activated снова ClaimForeground + Playing.  
4. Опционально: при hold Stereo всё равно держать SMTC Playing; проверить, не глушит ли silent MediaPlayer.

### C. Не трогать без нужды
- PolicyConfig / Hands-Free claim — в этой сессии OK (`hr=0`, peak высокий).

---

## 6. Ключевые цитаты

**Тест — реакция есть, смысл нет:**
```
2026-07-20 21:03:48.877 [INFO] HeadsetTest STT recognized: Law
2026-07-20 21:04:04.988 [INFO] HeadsetTest STT recognized: Dicks to test
2026-07-20 21:04:34.257 [INFO] HeadsetTest StopMicAsync begin
```

**Главное окно — то же:**
```
cultureReq=ru-RU
recognizer=… English - US
Recognized #1 conf=0.22 text=Team that law that he should season one coup for the full five
```

**Play — пусто:** нет строк `SMTC button` / `MediaPlay:` между 21:03 и 21:06 (кроме `MediaPlay: disposed` при закрытии).

---

## 7. Итог линии отчётов

| # | Проблема | Статус |
|---|----------|--------|
| 1 | Crash `Length` | Исправлено |
| 2–3 | Hang UI / нет STT | Исправлено (Fix #3 offline + no COM) |
| **4** | Текст не тот + **Play не ловится** | **Открыто** |

---

*Отчёт #4 по сессии 21:03–21:06 после Fix Pass #3.*
