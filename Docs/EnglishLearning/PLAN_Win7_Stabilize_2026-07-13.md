# План: стабилизация Hermes.EnglishLearning на Win7

**Дата работ:** 2026-07-13  
**Цель:** минимально стабильное приложение на флешке без обязательных сети/API; озвучка из локальных файлов; по шагам вернуть службы.

---

## Контекст (на сейчас)

| Симптом | Вероятная причина |
|--------|-------------------|
| Краш при старте (старые сборки) | WinRT SMTC / жёсткие Win10 API |
| Краш на «Озвучить» | WPF `MediaPlayer` + MP3 на Win7 |
| Установщик не закрывает процесс | Зависшее окно + JIT-отладчик |
| Сеть / Realtime в логах | Supabase недоступен или режется |

Уже сделано в ветке фикса (проверять версии **1.1.3+**): логи при старте, force-kill в Setup, SMTC через reflection / skip на Win7, Azure → WAV + SoundPlayer на Win7.

---

## Утро — установка и диагностика (без правок кода)

1. **Закрыть старые процессы**  
   - Закрыть Setup / JIT-отладчик.  
   - `taskkill /F /IM Hermes.EnglishLearning.exe /T`  
   - При необходимости — перезагрузка Win7.

2. **Поставить свежий Setup**  
   - `HermesEnglishLearning-Setup-1.1.3.exe` (или новее, если уже собран).  
   - Путь: `%LOCALAPPDATA%\Hermes.EnglishLearning\`

3. **Проверить лог сразу после запуска**  
   - `%LOCALAPPDATA%\Hermes.EnglishLearning\logs\english_learning_YYYYMMDD.log`  
   - Ожидать строки: OS, CLR, `SoundPlayer (Win7-safe WAV)` или `MediaPlayer`, `App start` / `UI loaded`.  
   - При краше — `crash_*.log` в той же папке; скопировать на флешку.

4. **Матрица коротких тестов (отмечать OK / FAIL)**  

   | # | Действие | Ожидание |
   |---|----------|----------|
   | A | Холодный старт | Окно + урок, без WER |
   | B | Навигация ← → | Смена экранов |
   | C | «Озвучить» (Azure) | Речь без краша |
   | D | Play на гарнитуре | Play/Pause без зависания |
   | E | «Кэш TTS» | Прогресс / файлы в `tts-cache` |
   | F | Отключить сеть (Wi‑Fi off) и повторить A–C | Работа офлайн |

5. **Решение по ветке дня**  
   - Если A–C OK → день на упрощение (см. ниже).  
   - Если A FAIL → смотреть лог/crash; не гонять Azure.  
   - Если только C FAIL → план «озвучка с флешки» (блок 2).

---

## День — упрощение до «флешка + локальные файлы»

Приоритет: **сначала стабильный офлайн-контур**, потом службы.

### Шаг 1. Офлайн-флаг / отключение сети (код)

- Выключатель в настройках или `settings.json`: `OfflineMode: true` / `EnableSupabase: false`.  
- При `true`: не поднимать Realtime/Supabase, не дергать Azure при Speak, если есть кэш.  
- В лог: `OfflineMode=ON` при старте.

### Шаг 2. Озвучка только из кэша (portable)

На **этой** (рабочей) машине:

1. Открыть урок → «Кэш TTS» → дождаться полного кэша.  
2. Скопировать на флешку целиком:  
   - `Hermes.EnglishLearning.exe` + dll/config  
   - `settings.json` (ключи можно оставить, но в Offline Speak не должен ходить в сеть)  
   - `SampleLessons\` / нужные `.md`  
   - папка **`tts-cache\`** (WAV после 1.1.3)  
3. На Win7: запуск с флешки или из `%LOCALAPPDATA%` после копирования кэша.  
4. Режим: «Speak = только локальные файлы»; при MISS — сообщение в Status/лог, **без краша** (SAPI fallback или тихий skip).

### Шаг 3. Временно убрать «шум» старта

Отключить или сделать опциональным:

- Startup greeting (SAPI при старте)  
- BT battery polling  
- Media focus / SMTC (на Win7 и так skip)  
- Prefetch/Azure при старте  

Оставить: урок MD, навигация, Speak из кэша, громкость, лог.

### Шаг 4. Документ «минимальный набор на флешке»

Короткий чеклист в `Docs/EnglishLearning/` (или README рядом с Setup):

```
exe + *.dll + *.config
settings.json          (OfflineMode=true)
SampleLessons\*.md
tts-cache\*.wav
logs\                  (создаётся сама)
```

---

## Вечер — если всё ещё нестабильно

1. **Профиль «SAPI only»**  
   - `TtsProvider: Sapi` в `settings.json`  
   - Проверка Speak без Azure/кэша.

2. **Сбор улик**  
   - Версия Setup / FileVersion exe  
   - Полный `english_learning_*.log` + `crash_*.log`  
   - Win7: разрядность (x86/x64), есть ли .NET 4.8, Windows Update / Media Feature Pack  

3. **Не делать завтра**  
   - Новые фичи UI, Python-порт, расширение Supabase.  
   - Только стабилизация и диагностика.

---

## Позже (после стабильного офлайна) — возврат служб по одному

Порядок включения (одна служба → тест → следующая):

1. Локальный SAPI (уже есть)  
2. Azure TTS + кэш (онлайн синтез только при MISS)  
3. Глобальный Play / громкость приложения  
4. Supabase Realtime (чтение)  
5. SMTC / BT battery (только Win10+)  

Критерий готовности к следующему шагу: **холодный старт + Speak без WER** на целевом Win7 три раза подряд.

---

## Чеклист результата на конец дня

- [ ] Setup 1.1.3+ установлен или portable `net48` с логом  
- [ ] Есть заполненная матрица A–F  
- [ ] OfflineMode (или ручное отключение Supabase) проверен без сети  
- [ ] На флешке лежит рабочий комплект + `tts-cache`  
- [ ] Список FAIL с путями к логам (если остались)

---

## Ссылки в репо

- Установщик: `Hermes.EnglishLearning/Installer/output/`  
- Логи (установка): `%LOCALAPPDATA%\Hermes.EnglishLearning\logs\`  
- Логи (portable): `<папка exe>\logs\`  
- TTS: `Docs/EnglishLearning/TTS_OPTIONS.md`
