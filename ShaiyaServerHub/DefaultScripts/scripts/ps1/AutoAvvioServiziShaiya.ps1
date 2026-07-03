# Definisci il percorso dello script e della cartella di log
$scriptPath = $PSScriptRoot
$logFolder = Join-Path -Path $scriptPath -ChildPath "log"
$logFileName = "AutoAvvioServiziShaiya.log"
$logFilePath = Join-Path -Path $logFolder -ChildPath $logFileName

# Crea la cartella di log se non esiste
if (-not (Test-Path -Path $logFolder)) {
    New-Item -Path $logFolder -ItemType Directory | Out-Null
}

# Controlla se i servizi sono in esecuzione
$requiredServices = @("PSM_AgentServer", "PSM_AgentClient")
$allRequiredServicesRunning = $true

foreach ($service in $requiredServices) {
    if ((Get-Service -Name $service -ErrorAction SilentlyContinue).Status -ne 'Running') {
        $allRequiredServicesRunning = $false
        break
    }
}

# Attendi 1 minuto se i servizi sono in esecuzione
if ($allRequiredServicesRunning) {
    Start-Sleep -Seconds 60

    # Avvia i servizi Shaiya Server
    $services = @("ps_session", "ps_userLog", "ps_login", "ps_gameLog", "ps_dbAgent", "ps_game")
    foreach ($service in $services) {
        Start-Service -Name $service -ErrorAction SilentlyContinue
        $message = "Servizio $service avviato."
        Add-Content -Path $logFilePath -Value "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) - $message"
    }
} else {
    $message = "I servizi richiesti non sono in esecuzione, non avvio i servizi Shaiya."
    Add-Content -Path $logFilePath -Value "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) - $message"
}
