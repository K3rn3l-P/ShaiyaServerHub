# PulisciLogServer.ps1
# Pulizia log del server - SOLO file piu' vecchi di $RetentionDays (default 7), mai un wipe totale:
# cosi' Test Suite/AI Hub (read_hub_file/read_server_file/read_service_log) possono ancora leggere la
# storia recente per diagnosi. Prima di questo fix cancellava TUTTO ad ogni run, azzerando anche log
# scritti pochi minuti prima.
#
# NB: C:\ShaiyaServer\PSM_Client\Bin\Log NON e' in elenco qui di proposito - e' gia' gestito da
# Control-ps_game.exe-Running.ps1 (comprime >2gg in .7z, poi elimina gli .7z >10gg) - se questo script
# lo pulisse ANCHE lui, i due task si pesterebbero i piedi: uno prova a conservare con criterio, l'altro
# cancellerebbe tutto. Verificato sul server reale (2026-07-14): il wipe totale precedente aveva gia'
# azzerato quella cartella nonostante l'altro task provasse a gestirla.
#
# NOTA: nessun carattere non-ASCII in questo file (vedi 22_autoruns_baseline.ps1 per il motivo: Windows
# PowerShell 5.1 legge i .ps1 con la codepage di sistema salvo BOM esplicito, un carattere non-ASCII in
# un file UTF-8-senza-BOM viene corrotto a runtime).
param([int]$RetentionDays = 7)

$scriptPath = $PSScriptRoot
$logFolder = Join-Path -Path $scriptPath -ChildPath "log"
$logFileName = "PulisciLogServer.log"
$logFilePath = Join-Path -Path $logFolder -ChildPath $logFileName

if (-not (Test-Path -Path $logFolder)) {
    New-Item -Path $logFolder -ItemType Directory | Out-Null
}

function Write-Log {
    param ([string]$message)
    Add-Content -Path $logFilePath -Value "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) - $message"
}

# Percorsi da pulire (PSM_Client\Bin\Log escluso di proposito, vedi commento in testa al file)
$logDirectories = @(
    "C:\ShaiyaServer\Logs",
    "C:\xampp\htdocs\config\logs",
    "C:\xampp\htdocs\Updater\logs",
    "C:\xampp\apache\logs"
)

function Clear-OldLogFiles {
    param (
        [string]$logDir,
        [int]$retentionDays
    )

    if (-not (Test-Path $logDir)) {
        Write-Log "SALTATO (non esiste): $logDir"
        return
    }

    $cutoff = (Get-Date).AddDays(-$retentionDays)
    $allFiles = Get-ChildItem -Path $logDir -Recurse -File -ErrorAction SilentlyContinue
    $toDelete = $allFiles | Where-Object { $_.LastWriteTime -lt $cutoff }
    $keptCount = $allFiles.Count - $toDelete.Count
    $freedBytes = ($toDelete | Measure-Object Length -Sum).Sum
    $freedMb = if ($freedBytes) { [math]::Round($freedBytes / 1MB, 2) } else { 0 }

    $deletedCount = 0
    $errorCount = 0
    foreach ($f in $toDelete) {
        try {
            Remove-Item -Path $f.FullName -Force -ErrorAction Stop
            $deletedCount++
        } catch {
            # File ancora in uso (es. log attivamente scritto da un servizio) o permessi - non e' un
            # errore fatale, il prossimo giro ci riprova. LastWriteTime di un file attivo resta comunque
            # sempre recente, quindi in pratica un file davvero in scrittura non finisce mai qui.
            $errorCount++
        }
    }

    $msg = "Pulizia $logDir - eliminati=$deletedCount mantenuti=$keptCount (piu' recenti di $retentionDays gg) liberati=${freedMb}MB"
    if ($errorCount -gt 0) { $msg += " errori=$errorCount" }
    Write-Log $msg
}

Write-Log "=== Avvio pulizia log (retention $RetentionDays giorni) ==="
foreach ($logDir in $logDirectories) {
    Clear-OldLogFiles -logDir $logDir -retentionDays $RetentionDays
}
Write-Log "=== Pulizia completata ==="
