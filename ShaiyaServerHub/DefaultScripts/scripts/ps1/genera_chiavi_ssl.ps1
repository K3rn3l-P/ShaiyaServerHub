# Definizione del percorso del comando di generazione delle chiavi SSL
$command = "tailscale cert --cert-file=C:\xampp\apache\conf\ssl.crt\server.crt --key-file=C:\xampp\apache\conf\ssl.key\server.key duff.pinto-lime.ts.net"

# Esecuzione del comando
Invoke-Expression -Command $command

# Notifica sul completamento dello script
Write-Host "Generazione delle chiavi SSL completata."

# Riavvio del servizio Apache per applicare le modifiche
Restart-Service -Name Apache2.4
Write-Host "Servizio Apache riavviato con successo."