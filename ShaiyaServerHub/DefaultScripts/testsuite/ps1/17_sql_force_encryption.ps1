# 17_sql_force_encryption.ps1
# Microsoft: forzare la cifratura delle connessioni SQL (ForceEncryption=1) protegge il traffico DB
# sulla LAN (credenziali/dati). WARN se ForceEncryption=0.
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$inst = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').'MSSQLSERVER'
if (-not $inst) {
    [pscustomobject]@{ Name = '17_sql_force_encryption'; Status = 'WARN'; Detail = 'istanza MSSQLSERVER non trovata' }
    return
}

$key  = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$inst\MSSQLServer\SuperSocketNetLib"
$fe   = [int](Get-ItemProperty $key).ForceEncryption
$cert = (Get-ItemProperty $key).Certificate

$detail = "ForceEncryption=$fe ; Certificate=" + $(if ($cert) { 'configurato' } else { 'nessuno (self-signed a runtime)' })
$status = if ($fe -eq 1) { 'PASS' } else { 'WARN' }
if ($fe -ne 1) { $detail += ' - traffico DB non forzato cifrato' }

[pscustomobject]@{ Name = '17_sql_force_encryption'; Status = $status; Detail = $detail }
