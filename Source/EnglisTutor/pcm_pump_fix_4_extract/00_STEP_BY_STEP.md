# Hermes.EnglishTutorClient — Fix Pass #4: culture/confidence + SMTC Play

Fix #3 (offline STT + UI без COM) закрыл hang и «мёртвый» STT. Отчёт #4 показал два новых,
**независимых** бага — не связаны друг с другом и не связаны с pump/UI-hang:

## A. Culture bug + нет confidence-порога
`cultureReq=ru-RU` запрашивается, но реально ставится `en-US` recognizer — отсюда бессмысленный
текст на любой не-английской или шумной речи. Плюс offline-путь (`OfflineSttRecognizer`) до сих
пор жёстко хардкодит `en-US` (TODO из Fix Pass #3, теперь закрываем). Ни один путь не отсекает
низкоуверенные результаты (`Confidence`), поэтому любой мусор доходит до UI/Supabase.

## B. Play на гарнитуре не долетает до Tutor
За сессию — ни одной строки SMTC/hotkey. Это НЕ аудио-баг (mic/STT работают), это гонка за
медиафокус (AVRCP Play уходит куда-то ещё, вероятно к другому приложению с активной
медиасессией) и/или потеря SMTC-подписки после reclaim аудио-эндпоинта.

## Файлы пакета

- `01_Culture_And_Confidence_Fix.md` — как правильно выбирать recognizer по `cultureReq` в
  `VoiceInputService` и в `OfflineSttRecognizer`, плюс порог confidence.
- `02_SMTC_Reclaim_Diagnostics.md` — логирование + принудительный re-claim `Playing`-статуса
  и переподписка `ButtonPressed` после каждого reclaim аудио-эндпоинта. Это **диагностический**
  фикс в первую очередь — если после него по-прежнему тишина в логе, значит фокус реально
  перехватывает другое приложение (P1), и это уже другая, более системная проблема (не то,
  что можно починить внутри Tutor).
- `cursor_prompt.txt` — промпт для агента: применить оба блока независимо, собрать, проверить
  вручную по инструкции из отчёта, самостоятельно написать
  `EnglishTutorClient_Fix4_Report.md` при любых нестыковках или если SMTC так и не заработает
  даже с диагностикой (это ожидаемый возможный исход для блока B).

## Что НЕ трогать
- `PcmPumpStream.cs`, offline-recognize архитектура из Fix #3 — не менять, только добавить
  culture-параметр и confidence-check.
- `AudioPolicyConfig.cs` — не трогать (в этой сессии `hr=0`, всё ок).
- `HeadsetAudioClaimer.cs` — можно только добавить вызов re-claim SMTC-статуса после reclaim,
  не менять саму логику claim/hold A2DP.
