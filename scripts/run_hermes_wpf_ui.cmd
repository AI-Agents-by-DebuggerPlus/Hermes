@echo off
setlocal
cd /d "%~dp0.."
dotnet build "%~dp0..\Hermes.Wpf.Launcher\Hermes.Wpf.Launcher.csproj" -c Debug --nologo
if errorlevel 1 exit /b %ERRORLEVEL%
start "" "%~dp0..\Hermes.Wpf.Launcher\bin\Debug\net8.0-windows\Hermes.Wpf.Launcher.exe"
exit /b 0
