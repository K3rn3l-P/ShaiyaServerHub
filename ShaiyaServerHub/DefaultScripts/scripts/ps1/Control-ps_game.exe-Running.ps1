# Definisci il percorso dello script e della cartella di log
$scriptPath = $PSScriptRoot
$logFolder = Join-Path -Path $scriptPath -ChildPath "log"
$logFileName = "Control-ps_game.log"
$logFilePath = Join-Path -Path $logFolder -ChildPath $logFileName

# Cartella dei log da gestire
$logDir = "C:\ShaiyaServer\PSM_Client\Bin\Log"

# Percorso eseguibile di 7zip
$sevenZipPath = "C:\Program Files\7-Zip\7z.exe"

# Crea la cartella di log se non esiste
if (-not (Test-Path -Path $logFolder)) {
    New-Item -Path $logFolder -ItemType Directory | Out-Null
}

# Funzione per scrivere nel log
function Write-Log {
    param (
        [string]$message
    )
    Add-Content -Path $logFilePath -Value "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) - $message"
}

# Funzione per comprimere i file più vecchi di 2 giorni
function Compress-OldFiles {
    param (
        [string]$sourceFolder,
        [string]$sevenZipExe
    )
    Write-Log "Inizio compressione dei file più vecchi di 2 giorni nella directory: $sourceFolder"

    $filesToCompress = Get-ChildItem -Path $sourceFolder -File | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-2) }

    foreach ($file in $filesToCompress) {
        try {
            $zipFileName = "$($file.FullName).7z"
            & $sevenZipExe a $zipFileName $file.FullName | Out-Null
            if (Test-Path -Path $zipFileName) {
                Remove-Item -Path $file.FullName -Force
                Write-Log "File compresso e rimosso: $($file.FullName)"
            } else {
                Write-Log "Errore durante la compressione: $($file.FullName)"
            }
        } catch {
            Write-Log "Errore durante la compressione del file $($file.FullName): $_"
        }
    }
    Write-Log "Compressione completata."
}

# Funzione per pulire i file compressi più vecchi di 10 giorni
function Cleanup-ZippedFiles {
    param (
        [string]$sourceFolder
    )
    Write-Log "Inizio pulizia dei file compressi più vecchi di 10 giorni nella directory: $sourceFolder"

    $zippedFilesToDelete = Get-ChildItem -Path $sourceFolder -File -Filter "*.7z" | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-10) }

    foreach ($file in $zippedFilesToDelete) {
        try {
            Remove-Item -Path $file.FullName -Force
            Write-Log "File compresso eliminato: $($file.FullName)"
        } catch {
            Write-Log "Errore durante l'eliminazione del file $($file.FullName): $_"
        }
    }
    Write-Log "Pulizia completata."
}

# Funzione principale per la gestione dei file
function Manage-LogFiles {
    param (
        [string]$logDir,
        [string]$sevenZipPath
    )
    Compress-OldFiles -sourceFolder $logDir -sevenZipExe $sevenZipPath
    Cleanup-ZippedFiles -sourceFolder $logDir
}

# Avvia la gestione dei file di log
Manage-LogFiles -logDir $logDir -sevenZipPath $sevenZipPath

# Definisci il nome del servizio associato al processo
$serviceName = "ps_game"

# Definisci i nomi dei task scheduler da avviare
$taskName1 = "AUTO-EXP-NOTIFY"
$taskName2 = "CancBossDeathLog"

# Timeout per il controllo (es. 15 minuti)
$timeout = 540

# Imposta l'ora di inizio
$startTime = Get-Date

# Verifica lo stato del servizio associato al processo
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service -ne $null -and $service.Status -eq "Running") {
    Write-Log "Il servizio $serviceName è già in esecuzione. Avvio dei task scheduler."

    # Delay di 20 secondi prima di avviare i task
    Start-Sleep -Seconds 20

    # Avvia i task scheduler
    Start-Process -FilePath "schtasks.exe" -ArgumentList "/run /tn $taskName1" -WindowStyle Hidden | Out-Null
    Start-Process -FilePath "schtasks.exe" -ArgumentList "/run /tn $taskName2" -WindowStyle Hidden | Out-Null
    Write-Log "Task scheduler avviati: $taskName1 e $taskName2."
} else {
    # Loop infinito per monitorare il servizio
    Write-Log "Il servizio $serviceName non è in esecuzione. In attesa che venga avviato..."

    while ($true) {
        # Verifica se il processo ps_game è in esecuzione
        $process = Get-Process -Name "ps_game" -ErrorAction SilentlyContinue
        if ($process -ne $null) {
            Write-Log "Processo ps_game trovato. In attesa che il servizio $serviceName venga avviato..."

            # Continua a verificare lo stato del servizio
            while ($service -eq $null -or $service.Status -ne 'Running') {
                Start-Sleep -Seconds 10
                $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
                if ($service -ne $null -and $service.Status -eq 'Running') {
                    Write-Log "Servizio $serviceName è stato avviato con successo."

                    # Delay di 20 secondi prima di avviare i task
                    Start-Sleep -Seconds 20

                    # Avvia i task scheduler
                    try {
                        Start-Process -FilePath "schtasks.exe" -ArgumentList "/run /tn $taskName1" -WindowStyle Hidden | Out-Null
                        Start-Process -FilePath "schtasks.exe" -ArgumentList "/run /tn $taskName2" -WindowStyle Hidden | Out-Null
                        Write-Log "Task scheduler avviati: $taskName1 e $taskName2."
                    } catch {
                        Write-Log "Errore durante l'avvio dei task scheduler: $_"
                    }
                    break  # Esci dal ciclo interno una volta avviati i task
                }
            }
            break  # Esci dal ciclo principale se il servizio è stato avviato e i task sono stati eseguiti
        } else {
            Write-Log "Il processo ps_game non è in esecuzione. Ripetere il controllo tra 10 secondi."
        }

        # Verifica il timeout
        if ((Get-Date) -ge $startTime.AddSeconds($timeout)) {
            Write-Log "Timeout raggiunto: il servizio $serviceName non è stato trovato o avviato entro il tempo massimo."
            break
        }

        Start-Sleep -Seconds 10  # Aspetta prima di ripetere il controllo
    }
}
