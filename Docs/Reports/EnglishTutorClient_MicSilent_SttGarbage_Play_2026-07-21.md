# Отчёт: mic silent / STT garbage / Play stop

**Дата:** 2026-07-21  
**Лог:** `%LOCALAPPDATA%\Hermes.EnglishTutorClient\logs\app.log`

## Вердикт по логу (до фикса)

| Симптом | Факт |
|---------|------|
| Тест гарнитуры — нет реакции | `peakMax=0.000`, нет `peak=` → WASAPI без сигнала |
| Главное окно — левый текст | Только **en-US** установлен; `cultureReq=ru-RU` → fallback; conf 0.04–0.19 |
| В чат ушёл мусор | `Stop` брал **hypothesis** (`wampum for the`), хотя finals отсеяны порогом 0.5 |
| Повторный Play | Первый Play (`SMTC Pause`) стартовал запись; стоп был с UI; после reclaim второго Play в логе нет |

## Причины

1. Тихий SMTC `MediaPlayer` держал **A2DP Stereo** даже после `PauseHoldForCapture` → Hands-Free mic = тишина.  
2. Пакет речи **ru-RU не установлен** (только en-US).  
3. `StopAndTakeText` отправлял неподтверждённую hypothesis.  
4. После mic reclaim SMTC снова крутил MediaPlayer; повторный AVRCP часто уходит в Chrome.

## Исправления

- `PauseSilentPlayerForMic` / `ResumeSilentPlayerAfterMic` + SCO near-silent hold на Hands-Free в тесте.  
- В чат только finals ≥ 0.5; hypothesis при стопе отбрасывается.  
- Reclaim гарнитуры пропускается пока идёт live STT.  
- UI теста показывает `✗` rejected сегменты.
