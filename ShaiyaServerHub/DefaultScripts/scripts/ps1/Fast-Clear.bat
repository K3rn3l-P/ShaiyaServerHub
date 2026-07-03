rem This script was written by k3rn3l - Published in 2023/24
rem Web: dc-inet.eu
cls

@echo off

COLOR 0A
TITLE Fast Clear - by K3rn3l
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

rem Check If User Has Admin Privileges
timeout /t 1 /nobreak > NUL
openfiles > NUL 2>&1
if %errorlevel%==0 (
    echo Running..
) else (
    echo You must run me as an Administrator. Exiting..
    echo.
    echo Right-click on me and select ^'Run as Administrator^' and try again.
    echo.
    echo Press any key to exit..
    pause > NUL
    exit
)

echo.

rem Delete Temporary Files
del /s /f /q %WinDir%\Temp\*.*
del /s /f /q %WinDir%\Prefetch\*.*
del /s /f /q %Temp%\*.*
del /s /f /q %AppData%\Temp\*.*
del /s /f /q %HomePath%\AppData\LocalLow\Temp\*.*

rem Delete Used Drivers Files (Not needed because already installed)
del /s /f /q %SYSTEMDRIVE%\AMD\*.*
del /s /f /q %SYSTEMDRIVE%\NVIDIA\*.*
del /s /f /q %SYSTEMDRIVE%\INTEL\*.*

rem Delete Temporary Folders
rd /s /q %WinDir%\Temp
rd /s /q %WinDir%\Prefetch
rd /s /q %Temp%
rd /s /q %AppData%\Temp
rd /s /q %HomePath%\AppData\LocalLow\Temp

rem Delete Used Drivers Folders (Not needed because already installed)
rd /s /q %SYSTEMDRIVE%\AMD
rd /s /q %SYSTEMDRIVE%\NVIDIA
rd /s /q %SYSTEMDRIVE%\INTEL

rem Recreate Empty Temporary Folders
md %WinDir%\Temp
md %WinDir%\Prefetch
md %Temp%
md %AppData%\Temp
md %HomePath%\AppData\LocalLow\Temp

rem Cleaning Chrome
taskkill /F /IM chrome.exe >nul 2>&1
rmdir /S /Q "%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache"
rmdir /S /Q "%LOCALAPPDATA%\Google\Chrome\User Data\Default\Code Cache"
del /q "%LocalAppData%\Google\Chrome\User Data\Default\Cache\*.*"
del /Q "%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cookies"
del /q "%LocalAppData%\Google\Chrome\User Data\Default\Network\Cookies"
del /q "%LocalAppData%\Google\Chrome\User Data\Default\History"
del /q "%LocalAppData%\Google\Chrome\User Data\Default\Sessions"

rem Cleaning Brave-Browser
taskkill /F /IM brave.exe >nul 2>&1
rmdir /S /Q "%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Cache"
rmdir /S /Q "%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Code Cache"
del /q "%LocalAppData%\BraveSoftware\Brave-Browser\User Data\Default\Cache\*.*"
del /Q "%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Cookies"
del /q "%LocalAppData%\BraveSoftware\Brave-Browser\User Data\Default\Network\Cookies"
del /q "%LocalAppData%\BraveSoftware\Brave-Browser\User Data\Default\History"
del /q "%LocalAppData%\BraveSoftware\Brave-Browser\User Data\Default\Sessions"

rem Cleaning Firefox...
taskkill /F /IM firefox.exe >nul 2>&1
del /q "%AppData%\Mozilla\Firefox\Profiles\%profilename%\cache2\*.*"
del /q "%AppData%\Mozilla\Firefox\Profiles\%profilename%\cookies.sqlite"
del /q "%AppData%\Mozilla\Firefox\Profiles\%profilename%\places.sqlite"
del /q "%AppData%\Mozilla\Firefox\Profiles\%profilename%\formhistory.sqlite
del /q "%AppData%\Mozilla\Firefox\Profiles\%profilename%\sessionstore.jsonlz4
del /Q "%APPDATA%\Mozilla\Firefox\Profiles\%profilename%\recovery.jsonlz4"

rem Cleaning Edge...
taskkill /F /IM msedge.exe >nul 2>&1
rmdir /s /q "%LocalAppData%\Microsoft\Edge\User Data\Default\Cache"
rmdir /s /q "%LocalAppData%\Microsoft\Edge\User Data\Default\Code Cache"
del /q "%LocalAppData%\Microsoft\Edge\User Data\Default\Cache\*.*"
del /Q "%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cookies"
del /q "%LocalAppData%\Microsoft\Edge\User Data\Default\Network\Cookies"
del /q "%LocalAppData%\Microsoft\Edge\User Data\Default\History"
del /q "%LocalAppData%\Google\Chrome\User Data\Default\Sessions"

rem Cleaning Internet Explorer...
taskkill /F /IM iexplore.exe >nul 2>&1
RunDll32.exe InetCpl.cpl,ClearMyTracksByProcess 1
RunDll32.exe InetCpl.cpl,ClearMyTracksByProcess 2
RunDll32.exe InetCpl.cpl,ClearMyTracksByProcess 8
set DataDir=C:\Users\%USERNAME%\AppData\Local\Microsoft\Intern~1 
del /q /s /f "%DataDir%" 
rd /s /q "%DataDir%" 
set History=C :\Users\%USERNAME%\AppData\Local\Microsoft\Windows\History 
del /q /s /f "%History%" 
rd /s /q "%History%" 
set IETemp=C:\Users\%USERNAME% \AppData\Local\Microsoft\Windows\Tempor~1 
del /q /s /f "%IETemp%" 
rd /s /q "%IETemp%" 
set Cookies=C:\Users\%USERNAME%\AppData\Roaming\ Microsoft\Windows\Cookies 
del /q /s /f "%Cookies%" 
rd /s /q "%Cookies%" 

rem Cleaning Opera...
taskkill /F /IM opera.exe >nul 2>&1
rmdir /s /q "%LocalAppData%\Opera Software\Opera Stable\Default\Cache"
rmdir /s /q "%LocalAppData%\Opera Software\Opera Stable\Default\Code Cache"
del /q "%AppData%\Opera Software\Opera Stable\Cache\*.*"
del /Q "%LOCALAPPDATA%\Opera Software\Opera Stable\Default\Cookies"
del /q "%AppData%\Opera Software\Opera Stable\Network\Cookies"
del /q "%AppData%\Opera Software\Opera Stable\History"
del /q "%LocalAppData%\Opera Software\Opera Stable\Default\Sessions"

rem Reset windows store for fast run.
WSreset.exe

rem Reset cache of DNS for fix issues.
ipconfig /flushdns

echo.
echo System file checker tool.
echo.
chkdsk
echo.

echo.
echo Running System File Checker (sfc)...
sfc /scannow

rem Check for errors and repair if necessary
if %errorlevel% neq 0 (
    echo System File Checker found errors. Running DISM to repair...
    DISM.exe /Online /Cleanup-image /Scanhealth
    DISM.exe /Online /Cleanup-image /Restorehealth

    rem If DISM.exe /Online /Cleanup-image /Restorehealth returns error 0x800f0906 or cannot find a valid source, proceed with the following:

    net stop wuauserv
    cd %systemroot%\SoftwareDistribution
    ren Download Download.old
    net start wuauserv
    net stop bits
    net start bits
    net stop cryptsvc
    cd %systemroot%\system32
    ren catroot2 catroot2old
    net start cryptsvc

    rem Run DISM.exe /Online /Cleanup-image /Restorehealth again
    DISM.exe /Online /Cleanup-image /Restorehealth
)

echo.
echo Windows Clean Up Done!, You can exit by pressing any key.
echo.

exit