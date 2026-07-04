@echo off
cls

COLOR 0C
TITLE Disable-Boost - k3rn3l Tools

ECHO          _            _                  _           _              _                  _
ECHO         /\_\        /\ \                /\ \        /\ \     _    /\ \                _\ \
ECHO        / / /  _    /  \ \              /  \ \      /  \ \   /\_\ /  \ \              /\__ \
ECHO       / / /  /\_\ / /\ \ \            / /\ \ \    / /\ \ \_/ / // /\ \ \            / /_ \_\
ECHO      / / /__/ / // / /\ \ \          / / /\ \_\  / / /\ \___/ // / /\ \ \          / / /\/_/
ECHO     / /\_____/ / \/_//_\ \ \        / / /_/ / / / / /  \/____/ \/_//_\ \ \        / / /
ECHO    / /\_______/    __\___ \ \      / / /__\/ / / / /    / / /    __\___ \ \      / / /
ECHO   / / /\ \ \      / /\   \ \ \    / / /_____/ / / /    / / /    / /\   \ \ \    / / / ____
ECHO  / / /  \ \ \    / /_/____\ \ \  / / /\ \ \  / / /    / / /    / /_/____\ \ \  / /_/_/ ___/\
ECHO / / /    \ \ \  /__________\ \ \/ / /  \ \ \/ / /    / / /    /__________\ \ \/_______/\__\/
ECHO \/_/      \_\_\ \_____________\/\/_/    \_\/\/_/     \/_/     \_____________\/\_______\/

ECHO   ______            __
ECHO  /_  __/___  ____  / /
ECHO   / / / __ \/ __ \/ /
ECHO  / / / /_/ / /_/ / /
ECHO /_/  \____/\____/_/

echo.
echo  =========================================================
echo   [ DISABLE BOOST ]  Ripristina impostazioni originali
echo  =========================================================
echo.

rem === Check Admin ===
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo  Errore: eseguire come Amministratore.
    echo  Tasto destro ^> Esegui come Amministratore
    echo.
    pause >nul
    exit /b 1
)

set "SCRIPTDIR=C:\script\RAM-CPU-Boost"
set "LOGFILE=%SCRIPTDIR%\boost.log"

echo [%date% %time%] [DISABLE] Inizio ripristino >> "%LOGFILE%"

rem ==================================================================
echo  [1/5] Ripristino servizi telemetria ai default Win11...
rem ==================================================================

sc config DiagTrack start= delayed-auto >nul 2>&1
sc start DiagTrack >nul 2>&1

sc config dmwappushservice start= demand >nul 2>&1
sc start dmwappushservice >nul 2>&1

sc config PcaSvc start= delayed-auto >nul 2>&1
sc start PcaSvc >nul 2>&1

sc config MapsBroker start= delayed-auto >nul 2>&1
sc start MapsBroker >nul 2>&1

sc config lfsvc start= demand >nul 2>&1

sc config RemoteRegistry start= demand >nul 2>&1

echo  Fatto.

rem ==================================================================
echo  [2/5] Ripristino SysMain ^(Superfetch^)...
rem ==================================================================

sc config SysMain start= auto >nul 2>&1
sc start SysMain >nul 2>&1
echo  SysMain riabilitato.

rem Standby List + Process Priority: temporanei, nessuna azione necessaria.

rem ==================================================================
echo  [3/5] Ripristino effetti visivi...
rem ==================================================================

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" /v VisualFXSetting /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" /v EnableTransparency /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKCU\Control Panel\Desktop\WindowMetrics" /v MinAnimate /t REG_SZ /d 1 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v TaskbarAnimations /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v ListviewAlphaSelect /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v ListviewShadow /t REG_DWORD /d 1 /f >nul 2>&1
echo  Fatto.

rem ==================================================================
echo  [4/5] Ripristino Background App Permissions...
rem ==================================================================

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications" /v GlobalUserDisabled /f >nul 2>&1
echo  Fatto.

rem ==================================================================
echo  [5/5] Restart Explorer per applicare effetti visivi subito...
rem ==================================================================

taskkill /f /im explorer.exe >nul 2>&1
timeout /t 1 /nobreak >nul
start explorer.exe
echo  Explorer riavviato.

echo [%date% %time%] [DISABLE] Ripristino completato >> "%LOGFILE%"

echo.
echo  =========================================================
echo   Ripristino completato.
echo   Nota: VisualFXSetting completo attivo al prossimo logon.
echo   Log: %LOGFILE%
echo  =========================================================
echo.
pause >nul
exit /b 0
