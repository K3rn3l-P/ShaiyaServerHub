# Definisci il percorso della cartella contenente i file XML
$xmlFolderPath = "F:\1.Task Scheduler BACKUP"

# Ottieni tutti i file XML nella cartella
$xmlFiles = Get-ChildItem -Path $xmlFolderPath -Filter *.xml

# Ottieni il nome utente attualmente connesso
$username = $env:USERNAME

# Definisci la password (se necessario)
$password = "Va9$-TI3%"  # Sostituisci con la tua password

# Crea un oggetto SecureString per la password
$securePassword = ConvertTo-SecureString $password -AsPlainText -Force

# Crea un oggetto di credenziali
$credential = New-Object System.Management.Automation.PSCredential($username, $securePassword)

# Importa i file XML come attività
foreach ($xmlFile in $xmlFiles) {
    $taskName = [System.IO.Path]::GetFileNameWithoutExtension($xmlFile.Name)
    $xmlFilePath = $xmlFile.FullName
    
    # Usa schtasks per importare il task
    schtasks /Create /TN $taskName /XML $xmlFilePath /F /RU $credential.UserName /RP $credential.GetNetworkCredential().Password

    Write-Host "Importato task: $taskName"
}

Write-Host "Importazione completata."
