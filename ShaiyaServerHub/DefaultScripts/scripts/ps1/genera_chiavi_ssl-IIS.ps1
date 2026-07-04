# Definizione del percorso del comando di generazione delle chiavi SSL per Tailscale
$command = "tailscale cert --cert-file=C:\inetpub\ssl\server.crt --key-file=C:\inetpub\ssl\server.key duff.pinto-lime.ts.net"

# Esecuzione del comando per generare le chiavi SSL
Invoke-Expression -Command $command

# Verifica se la generazione delle chiavi è andata a buon fine
$crtExists = Test-Path "C:\inetpub\ssl\server.crt"
$keyExists = Test-Path "C:\inetpub\ssl\server.key"

if ($crtExists -and $keyExists) {
    Write-Host "Generazione delle chiavi SSL completata."

    # Creazione del file .pfx usando OpenSSL
    $pfxCommand = "openssl pkcs12 -export -out C:\inetpub\ssl\server.pfx -inkey C:\inetpub\ssl\server.key -in C:\inetpub\ssl\server.crt -passout pass:Vh9$-TI3%"

    # Esecuzione del comando OpenSSL per creare il file .pfx
    Invoke-Expression -Command $pfxCommand

    # Verifica se il file .pfx è stato creato correttamente
    if (Test-Path "C:\inetpub\ssl\server.pfx") {
        Write-Host "Creazione del file PFX completata con successo."
    } else {
        Write-Host "Errore nella creazione del file PFX."
    }
} else {
    Write-Host "Errore nella generazione delle chiavi SSL."
}
