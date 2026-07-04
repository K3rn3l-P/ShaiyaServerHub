# Definizione del percorso del comando di generazione delle chiavi SSL per Tailscale
$command = "tailscale cert --cert-file=C:\xampp\apache\conf\ssl.crt\server.crt --key-file=C:\xampp\apache\conf\ssl.key\server.key duff.pinto-lime.ts.net"

# Esecuzione del comando per generare le chiavi SSL
Invoke-Expression -Command $command

# Verifica se la generazione delle chiavi è andata a buon fine
$crtExists = Test-Path "C:\xampp\apache\conf\ssl.crt\server.crt"
$keyExists = Test-Path "C:\xampp\apache\conf\ssl.key\server.key"

if ($crtExists -and $keyExists) {
    Write-Host "Generazione delle chiavi SSL completata."

} else {
    Write-Host "Errore nella generazione delle chiavi SSL."
}

# Riavvio del servizio Apache per applicare le modifiche
Restart-Service -Name Apache2.4
Write-Host "Servizio Apache riavviato con successo."