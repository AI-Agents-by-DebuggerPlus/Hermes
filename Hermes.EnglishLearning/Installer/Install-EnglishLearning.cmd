@echo off
setlocal
set APPDIR=%LOCALAPPDATA%\Hermes.EnglishLearning
echo Installing Hermes English Learning to %APPDIR% ...
if not exist "%APPDIR%" mkdir "%APPDIR%"
xcopy /E /I /Y /Q "%~dp0*" "%APPDIR%\" >nul
del /Q "%APPDIR%\Install-EnglishLearning.cmd" 2>nul

set STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
if not exist "%STARTUP%" mkdir "%STARTUP%"
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Startup') + '\Hermes English Learning.lnk'); $s.TargetPath=$env:LOCALAPPDATA + '\Hermes.EnglishLearning\Hermes.EnglishLearning.exe'; $s.WorkingDirectory=$env:LOCALAPPDATA + '\Hermes.EnglishLearning'; $s.Save()"
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v HermesEnglishLearning /t REG_SZ /d "\"%APPDIR%\Hermes.EnglishLearning.exe\"" /f >nul

echo Starting application...
start "" "%APPDIR%\Hermes.EnglishLearning.exe"
echo Done.
pause
