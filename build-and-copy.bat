@echo off
REM Doppio click: builda Release + copia gli script di default (con password dal .env)
REM nella cartella bin\Release\net48. Per Debug:  build-and-copy.bat Debug
REM Path relativi: %~dp0 = cartella di questo .bat (root del repo).
setlocal
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ShaiyaServerHub\Build-And-Copy.ps1" -Config %CONFIG%
echo.
pause
