# Imposta le variabili
$RootPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$BackupPath = Join-Path -Path $RootPath -ChildPath "AutoBackUp"
$LogPath = Join-Path -Path $RootPath -ChildPath "log\Re.Move-OLDBackupFiles.log"
$SevenZipPath = "C:\Program Files\7-Zip\7z.exe"

# Controlla se 7-Zip esiste
if (-Not (Test-Path $SevenZipPath)) {
    Write-Output "$([datetime]::Now) - Errore: 7-Zip non trovato nel percorso predefinito." | Out-File -FilePath $LogPath -Append
    exit
}

# Crea la cartella di log se non esiste
if (-Not (Test-Path (Split-Path -Parent $LogPath))) {
    New-Item -Path (Split-Path -Parent $LogPath) -ItemType Directory
}

# Scrivi log
"$([datetime]::Now) - Inizio esecuzione" | Out-File -FilePath $LogPath -Append

# Crea le cartelle se non esistono
$folders = @("NEW", "OLD", "LAST30")
foreach ($folder in $folders) {
    $folderPath = Join-Path -Path $BackupPath -ChildPath $folder
    if (-Not (Test-Path $folderPath)) {
        New-Item -Path $folderPath -ItemType Directory
    }
}

# Compressione e cancellazione dei file nella cartella NEW
$backupFiles = Get-ChildItem -Path (Join-Path -Path $BackupPath -ChildPath "NEW") -Filter *.bak
foreach ($file in $backupFiles) {
    if ($file.LastWriteTime -lt (Get-Date).AddDays(-3)) {
        # Compressione del file .bak
        $compressedFileName = "$($file.BaseName)_$($file.LastWriteTime.ToString('yyyyMMdd')).7z"
        & $SevenZipPath a -t7z -m0=lzma2 -mx=5 -mfb=128 -md=100m -ms=on -mmt=2 (Join-Path -Path $BackupPath -ChildPath "NEW\$compressedFileName") $file.FullName
        
        # Cancellazione del file .bak
        Remove-Item $file.FullName -Force

        # Spostamento del file compresso in OLD
        Move-Item (Join-Path -Path $BackupPath -ChildPath "NEW\$compressedFileName") (Join-Path -Path $BackupPath -ChildPath "OLD")
        "$([datetime]::Now) - Compressi e cancellati file .bak piu vecchi di 3 giorni da NEW, spostati in OLD come .7z" | Out-File -FilePath $LogPath -Append
    }
}

# Elimina i file piu' vecchi di 4 giorni nella cartella NEW
$oldBackupFiles = Get-ChildItem -Path (Join-Path -Path $BackupPath -ChildPath "NEW") -Filter *.bak
foreach ($file in $oldBackupFiles) {
    if ($file.LastWriteTime -lt (Get-Date).AddDays(-4)) {
        Remove-Item $file.FullName -Force
        "$([datetime]::Now) - Eliminati file .bak piu' vecchi di 4 giorni da NEW" | Out-File -FilePath $LogPath -Append
    }
}

# Sposta i file compressi (.7z) piu' vecchi di 10 giorni da OLD a LAST30
$oldCompressedFiles = Get-ChildItem -Path (Join-Path -Path $BackupPath -ChildPath "OLD") -Filter *.7z
foreach ($file in $oldCompressedFiles) {
    if ($file.LastWriteTime -lt (Get-Date).AddDays(-10)) {
        Move-Item $file.FullName (Join-Path -Path $BackupPath -ChildPath "LAST30")
        "$([datetime]::Now) - Spostati file .7z piu' vecchi di 10 giorni da OLD a LAST30" | Out-File -FilePath $LogPath -Append
    }
}

# Elimina i file compressi (.7z) piu' vecchi di 11 giorni dalla cartella OLD
foreach ($file in $oldCompressedFiles) {
    if ($file.LastWriteTime -lt (Get-Date).AddDays(-11)) {
        Remove-Item $file.FullName -Force
        "$([datetime]::Now) - Eliminati file .7z piu' vecchi di 11 giorni da OLD" | Out-File -FilePath $LogPath -Append
    }
}

# Elimina i file compressi (.7z) piu' vecchi di 30 giorni dalla cartella LAST30
$last30CompressedFiles = Get-ChildItem -Path (Join-Path -Path $BackupPath -ChildPath "LAST30") -Filter *.7z
foreach ($file in $last30CompressedFiles) {
    if ($file.LastWriteTime -lt (Get-Date).AddDays(-30)) {
        Remove-Item $file.FullName -Force
        "$([datetime]::Now) - Eliminati file .7z piu' vecchi di 30 giorni da LAST30" | Out-File -FilePath $LogPath -Append
    }
}

"$([datetime]::Now) - Esecuzione completata" | Out-File -FilePath $LogPath -Append
