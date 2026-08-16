@echo off
REM Rebuild + launch Hermes.Wpf (CLI). For button UI use run_hermes_wpf_ui.cmd
setlocal
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run_hermes_wpf.ps1" %*
exit /b %ERRORLEVEL%
