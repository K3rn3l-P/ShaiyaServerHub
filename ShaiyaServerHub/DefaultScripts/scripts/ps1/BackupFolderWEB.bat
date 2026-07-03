@echo off

rem Imposta la cartella di origine e la cartella di destinazione del backup
set "SourceFolder=C:\xampp\htdocs"
set "BackupFolder=F:\BackupAutomatici\ShaiyaWebBK"
 
rem Crea la cartella di destinazione se non esiste
if not exist "%BackupFolder%" (
    mkdir "%BackupFolder%"
)

rem Elimina i file .7z più vecchi di una settimana
forfiles /p "%BackupFolder%" /s /m *.7z /d -7 /c "cmd /c del @path"

rem Elimina i file .log più vecchi di una settimana
forfiles /p "%BackupFolder%" /s /m *.log /d -7 /c "cmd /c del @path"

rem Ottieni la data e l'ora correnti nel formato desiderato
for /f "tokens=2 delims==" %%I in ('wmic os get localdatetime /value') do set "DateTime=%%I"
set "Date=%DateTime:~6,2%-%DateTime:~4,2%-%DateTime:~0,4%"
set "Time=%DateTime:~8,2%.%DateTime:~10,2%.%DateTime:~12,2%"

rem Crea una variabile con il nome della cartella madre
for %%I in ("%SourceFolder%") do set "ParentFolderName=%%~nxI"

rem Esegui la compressione utilizzando 7-Zip con un livello di compressione maggiore
set "LogFilePath=%BackupFolder%\%ParentFolderName%_%Date%_%Time%.log"
"C:\Program Files\7-Zip\7z.exe" a -t7z -m0=lzma2 -mx=5 -mfb=128 -md=100m -ms=on -mmt=2 "%BackupFolder%\%ParentFolderName%_%Date%_%Time%.7z" "%SourceFolder%\*" > "%LogFilePath%"

rem Registra l'operazione di backup nel file di log
echo %DATE% %TIME% - Backup completato per la cartella %ParentFolderName%. >> "%BackupFolder%\BackupLog.txt"

echo Backup completato.
