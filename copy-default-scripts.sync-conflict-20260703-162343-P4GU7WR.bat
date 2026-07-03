@echo off
REM Copia gli script di default (DefaultScripts) + inietta le password dal .env.
REM Uso:  copy-default-scripts.bat  [cartella_destinazione]
REM Senza argomento usa la build Release.
setlocal
set "PROJ=%~dp0ShaiyaServerHub"
set "DEST=%~1"
if "%DEST%"=="" set "DEST=%PROJ%\bin\Release\net48\"
powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJ%\Build-Scripts.ps1" -ProjectDir "%PROJ%" -OutDir "%DEST%"
echo.
pause
