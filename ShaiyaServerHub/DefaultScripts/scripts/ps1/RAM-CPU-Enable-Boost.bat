@echo off
cls

COLOR 0B
TITLE Enable-Boost - k3rn3l Tools

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
echo   [ ENABLE BOOST ]  Applica ottimizzazioni RAM ^& CPU
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

rem === Setup ===
set "SCRIPTDIR=C:\script\RAM-CPU-Boost"
set "LOGFILE=%SCRIPTDIR%\boost.log"
set "TMPPS1=%TEMP%\_boost_tmp.ps1"

rem === Cattura RAM iniziale ===
for /f "tokens=*" %%r in ('PowerShell -NoProfile -Command "[Math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024)"') do set RAM_BEFORE=%%r
if "%RAM_BEFORE%"=="" set RAM_BEFORE=0

echo [%date% %time%] [ENABLE] Inizio - RAM libera: %RAM_BEFORE% MB >> "%LOGFILE%"
echo  RAM libera all'avvio: %RAM_BEFORE% MB
echo.

rem ==================================================================
echo  [1/7] Servizi telemetria: stop + Manual/Disabled...
rem ==================================================================

sc config DiagTrack start= demand >nul 2>&1
net stop DiagTrack >nul 2>&1

sc config dmwappushservice start= disabled >nul 2>&1
net stop dmwappushservice >nul 2>&1

sc config PcaSvc start= demand >nul 2>&1
net stop PcaSvc >nul 2>&1

sc config MapsBroker start= disabled >nul 2>&1
net stop MapsBroker >nul 2>&1

sc config lfsvc start= demand >nul 2>&1
net stop lfsvc >nul 2>&1

sc config RemoteRegistry start= disabled >nul 2>&1
net stop RemoteRegistry >nul 2>&1

echo  Fatto.

rem ==================================================================
echo  [2/7] SysMain ^(Superfetch^): rilevamento SSD/HDD...
rem ==================================================================

echo if ((Get-PhysicalDisk ^| Where-Object {$_.MediaType -eq 'SSD'}).Count -gt 0) { 'SSD' } else { 'HDD' } > "%TMPPS1%"
for /f "tokens=*" %%s in ('PowerShell -NoProfile -ExecutionPolicy Bypass -File "%TMPPS1%"') do set DISK_TYPE=%%s
del "%TMPPS1%" >nul 2>&1

if /i "%DISK_TYPE%"=="SSD" (
    sc config SysMain start= disabled >nul 2>&1
    net stop SysMain >nul 2>&1
    echo  SSD rilevato: SysMain disabilitato ^(risparmio 100-400 MB^).
) else (
    echo  HDD rilevato: SysMain lasciato attivo.
)

rem ==================================================================
echo  [3/7] Empty Standby List...
rem ==================================================================

if exist "%SCRIPTDIR%\EmptyStandbyList.exe" (
    echo  Usando EmptyStandbyList.exe ^(Wagnardsoft^)...
    "%SCRIPTDIR%\EmptyStandbyList.exe" modifiedpagelist >nul 2>&1
    "%SCRIPTDIR%\EmptyStandbyList.exe" standbylist >nul 2>&1
    "%SCRIPTDIR%\EmptyStandbyList.exe" priority0standbylist >nul 2>&1
) else (
    echo  Usando PowerShell NtSetSystemInformation ^(nativo^)...
    echo $sig = '[DllImport("ntdll.dll")] public static extern uint NtSetSystemInformation(int c, IntPtr p, int l);' > "%TMPPS1%"
    echo try { $t = Add-Type -MemberDefinition $sig -Name 'Ntdll' -Namespace 'Win32' -PassThru } catch { $t = [Win32.Ntdll] } >> "%TMPPS1%"
    echo $p = [System.Runtime.InteropServices.Marshal]::AllocHGlobal(4) >> "%TMPPS1%"
    echo [System.Runtime.InteropServices.Marshal]::WriteInt32($p, 3) >> "%TMPPS1%"
    echo $null = $t::NtSetSystemInformation(80, $p, 4) >> "%TMPPS1%"
    echo [System.Runtime.InteropServices.Marshal]::WriteInt32($p, 4) >> "%TMPPS1%"
    echo $null = $t::NtSetSystemInformation(80, $p, 4) >> "%TMPPS1%"
    echo [System.Runtime.InteropServices.Marshal]::WriteInt32($p, 5) >> "%TMPPS1%"
    echo $null = $t::NtSetSystemInformation(80, $p, 4) >> "%TMPPS1%"
    echo [System.Runtime.InteropServices.Marshal]::FreeHGlobal($p) >> "%TMPPS1%"
    PowerShell -NoProfile -ExecutionPolicy Bypass -File "%TMPPS1%" 2>nul
    del "%TMPPS1%" >nul 2>&1
)
echo  Standby List svuotata.

rem ==================================================================
echo  [4/7] Priorita' processi background: BELOW_NORMAL...
rem ==================================================================

echo $procs = @('chrome','msedge','brave','Discord','Teams','OneDrive','Spotify','EpicGamesLauncher','steamwebhelper','slack','zoom','notion') > "%TMPPS1%"
echo foreach ($name in $procs) { >> "%TMPPS1%"
echo     Get-Process -Name $name -ErrorAction SilentlyContinue ^| ForEach-Object { >> "%TMPPS1%"
echo         try { $_.PriorityClass = 'BelowNormal' } catch {} >> "%TMPPS1%"
echo     } >> "%TMPPS1%"
echo } >> "%TMPPS1%"
PowerShell -NoProfile -ExecutionPolicy Bypass -File "%TMPPS1%" 2>nul
del "%TMPPS1%" >nul 2>&1
echo  Fatto.

rem ==================================================================
echo  [5/7] Effetti visivi: Best Performance via registry...
rem ==================================================================

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" /v VisualFXSetting /t REG_DWORD /d 2 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" /v EnableTransparency /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Control Panel\Desktop\WindowMetrics" /v MinAnimate /t REG_SZ /d 0 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v TaskbarAnimations /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v ListviewAlphaSelect /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v ListviewShadow /t REG_DWORD /d 0 /f >nul 2>&1
echo  Fatto. Alcune animazioni attive dal prossimo logon.

rem ==================================================================
echo  [6/7] Background App Permissions: Off globale...
rem ==================================================================

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications" /v GlobalUserDisabled /t REG_DWORD /d 1 /f >nul 2>&1
echo  Fatto.

rem ==================================================================
echo  [7/7] Report RAM...
rem ==================================================================

for /f "tokens=*" %%r in ('PowerShell -NoProfile -Command "[Math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024)"') do set RAM_AFTER=%%r
if "%RAM_AFTER%"=="" set RAM_AFTER=0
set /a RAM_GAINED=%RAM_AFTER%-%RAM_BEFORE%

echo.
echo  =========================================================
echo   RISULTATI
echo   RAM prima  : %RAM_BEFORE% MB liberi
echo   RAM dopo   : %RAM_AFTER% MB liberi
echo   Guadagno   : +%RAM_GAINED% MB
echo  =========================================================

echo [%date% %time%] [ENABLE] Fine - RAM dopo: %RAM_AFTER% MB ^| Guadagno: +%RAM_GAINED% MB >> "%LOGFILE%"

echo.
echo  Per ripristinare: esegui Disable-Boost.bat
echo  Log: %LOGFILE%
echo.
pause >nul
exit /b 0
