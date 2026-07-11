# Imposta il percorso dello script e della cartella di log
$scriptPath = $PSScriptRoot
$logFolder = Join-Path -Path $scriptPath -ChildPath "log"
$logFileName = "PulisciLogServer.log"
$logFilePath = Join-Path -Path $logFolder -ChildPath $logFileName

# Crea la cartella di log se non esiste
if (-not (Test-Path -Path $logFolder)) {
    New-Item -Path $logFolder -ItemType Directory | Out-Null
}

# Percorsi da pulire
$logDirectories = @(
    "C:\ShaiyaServer\PSM_Client\Bin\Log",
    "C:\ShaiyaServer\Logs",
    "C:\xampp\htdocs\config\logs",
    "C:\xampp\htdocs\Updater\logs",
    "C:\xampp\apache\logs"
)

# Funzione per pulire i file di log
function Clear-LogFiles {
    param (
        [string]$logDir
    )

    if (Test-Path $logDir) {
        # Elimina tutti i file nelle cartelle e sottocartelle
        Get-ChildItem -Path $logDir -Recurse -File | Remove-Item -Force -ErrorAction SilentlyContinue
        $message = "Pulizia completata per: $logDir"
    } else {
        $message = "La cartella $logDir non esiste."
    }
    
    # Registra il messaggio nel file di log
    Add-Content -Path $logFilePath -Value "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) - $message"
}

# Pulisce ciascuna directory di log
foreach ($logDir in $logDirectories) {
    Clear-LogFiles -logDir $logDir
}
