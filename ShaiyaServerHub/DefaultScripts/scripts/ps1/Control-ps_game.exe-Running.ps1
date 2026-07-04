# Control-ps_game.exe-Running.ps1
# Orchestratore: comprime i log del server e, quando il servizio ps_game e' UP,
# avvia i task dipendenti. NB: i task del Hub vivono in \ShaiyaServerHub\ (NON in root):
# schtasks /run vuole il PATH COMPLETO, altrimenti "task non trovato" e fallisce in silenzio.

# Percorso dello script e cartella di log
$scriptPath = $PSScriptRoot
$logFolder = Join-Path -Path $scriptPath -ChildPath "log"
$logFileName = "Control-ps_game.log"
$logFilePath = Join-Path -Path $logFolder -ChildPath $logFileName

# Cartella dei log del server da gestire
$logDir = "C:\ShaiyaServer\PSM_Client\Bin\Log"

# Percorso eseguibile di 7zip
$sevenZipPath = "C:\Program Files\7-Zip\7z.exe"

# ── Cartella del Task Scheduler in cui vivono i task del Hub ──
$taskFolder = "\ShaiyaServerHub\"

# Nomi dei task dipendenti da avviare (SENZA path: viene aggiunto $taskFolder sotto)
$taskName1 = "AUTO-EXP-NOTIFY"
$taskName2 = "CancBossDeathLog"

# Timeout per il controllo (secondi)
$timeout = 540

if (-not (Test-Path -Path $logFolder)) {
    New-Item -Path $logFolder -ItemType Directory | Out-Null
}

function Write-Log {
    param ([string]$message)
    Add-Content -Path $logFilePath -Value "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) - $message"
}

# Avvia un task del Hub col PATH COMPLETO e logga l'esito REALE (exit code di schtasks).
function Start-HubTask {
    param ([string]$shortName)
    $full = $taskFolder + $shortName
    & schtasks.exe /run /tn "$full" *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Log "Task avviato: $full"
    } else {
        Write-Log "ERRORE avvio task (exit $LASTEXITCODE): $full  <-- verifica che il task esista in $taskFolder"
    }
}

function Start-DependentTasks {
    Start-HubTask -shortName $taskName1
    Start-HubTask -shortName $taskName2
}

function Compress-OldFiles {
    param ([string]$sourceFolder, [string]$sevenZipExe)
    Write-Log "Inizio compressione file piu' vecchi di 2 giorni in: $sourceFolder"
    $filesToCompress = Get-ChildItem -Path $sourceFolder -File | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-2) }
    foreach ($file in $filesToCompress) {
        try {
            $zipFileName = "$($file.FullName).7z"
            & $sevenZipExe a $zipFileName $file.FullName | Out-Null
            if (Test-Path -Path $zipFileName) {
                Remove-Item -Path $file.FullName -Force
                Write-Log "File compresso e rimosso: $($file.FullName)"
            } else {
                Write-Log "Errore compressione: $($file.FullName)"
            }
        } catch {
            Write-Log "Errore compressione file $($file.FullName): $_"
        }
    }
    Write-Log "Compressione completata."
}

function Cleanup-ZippedFiles {
    param ([string]$sourceFolder)
    Write-Log "Inizio pulizia .7z piu' vecchi di 10 giorni in: $sourceFolder"
    $zippedFilesToDelete = Get-ChildItem -Path $sourceFolder -File -Filter "*.7z" | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-10) }
    foreach ($file in $zippedFilesToDelete) {
        try {
            Remove-Item -Path $file.FullName -Force
            Write-Log "File .7z eliminato: $($file.FullName)"
        } catch {
            Write-Log "Errore eliminazione $($file.FullName): $_"
        }
    }
    Write-Log "Pulizia completata."
}

function Manage-LogFiles {
    param ([string]$logDir, [string]$sevenZipPath)
    Compress-OldFiles -sourceFolder $logDir -sevenZipExe $sevenZipPath
    Cleanup-ZippedFiles -sourceFolder $logDir
}

# ── 1) Manutenzione log ──
Manage-LogFiles -logDir $logDir -sevenZipPath $sevenZipPath

# ── 2) Attendi che il servizio ps_game sia UP, poi avvia i task dipendenti ──
$serviceName = "ps_game"
$startTime = Get-Date
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service -ne $null -and $service.Status -eq "Running") {
    Write-Log "Servizio $serviceName gia' in esecuzione. Avvio dei task dipendenti."
    Start-Sleep -Seconds 20
    Start-DependentTasks
} else {
    Write-Log "Servizio $serviceName non in esecuzione. In attesa..."
    while ($true) {
        $process = Get-Process -Name "ps_game" -ErrorAction SilentlyContinue
        if ($process -ne $null) {
            Write-Log "Processo ps_game trovato. Attendo che il servizio sia Running..."
            while ($service -eq $null -or $service.Status -ne 'Running') {
                Start-Sleep -Seconds 10
                $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
                if ($service -ne $null -and $service.Status -eq 'Running') {
                    Write-Log "Servizio $serviceName avviato. Avvio dei task dipendenti."
                    Start-Sleep -Seconds 20
                    Start-DependentTasks
                    break
                }
                if ((Get-Date) -ge $startTime.AddSeconds($timeout)) { break }
            }
            break
        } else {
            Write-Log "Processo ps_game non in esecuzione. Ricontrollo tra 10 secondi."
        }
        if ((Get-Date) -ge $startTime.AddSeconds($timeout)) {
            Write-Log "Timeout raggiunto: servizio $serviceName non trovato/avviato in tempo."
            break
        }
        Start-Sleep -Seconds 10
    }
}
