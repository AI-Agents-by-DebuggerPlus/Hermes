# Hermes English Learning — installer (Windows 7+)

## Голосовое приветствие

RU: «EnglishLearning готово к работе.»  
EN: **EnglishLearning is ready to work.**

## Сборка установщика

На машине разработчика (нужен .NET SDK):

```powershell
cd Hermes.EnglishLearning\Installer
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

Результат:

- `Installer\output\HermesEnglishLearning-Setup-1.1.0.exe` — если найден/установлен Inno Setup  
- или `Installer\output\HermesEnglishLearning-1.1.0-Win7.zip` + `Install-EnglishLearning.cmd`

## Что делает установщик

1. Копирует приложение в `%LOCALAPPDATA%\Hermes.EnglishLearning` (без прав администратора).
2. Добавляет в автозагрузку (папка Startup + `HKCU\...\Run`).
3. Запускает приложение сразу после установки.
4. При каждом запуске — голосовое приветствие на английском (SAPI).

## Требования на целевом ПК (Win7)

- .NET Framework 4.8 (или 4.7.2+ в большинстве случаев для net48)
- Для Azure Neural TTS нужен Win10+ / интернет; на Win7 работает SAPI + заранее заполненный `tts-cache`
