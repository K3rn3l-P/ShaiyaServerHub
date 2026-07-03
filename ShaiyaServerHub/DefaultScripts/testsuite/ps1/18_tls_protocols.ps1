# 18_tls_protocols.ps1
# Hardening trasporto (Schannel): SSL 2.0/3.0 e TLS 1.0/1.1 devono essere disabilitati, TLS 1.2 abilitato.
# WARN se un protocollo debole e' ESPLICITAMENTE abilitato o se TLS 1.2 e' esplicitamente disabilitato.
# 'default' (chiave assente) = comportamento OS, non penalizzato (Windows recenti disabilitano i deboli di default).
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$base = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols'

function Get-ProtoState([string]$name) {
    $p = Join-Path $base "$name\Server"
    $en = (Get-ItemProperty $p).Enabled
    if ($null -eq $en) { return 'default' }
    elseif ([int]$en -eq 0) { return 'disabled' }
    else { return 'enabled' }
}

$weak = 'SSL 2.0', 'SSL 3.0', 'TLS 1.0', 'TLS 1.1'
$lines = @()
$bad = 0
foreach ($w in $weak) {
    $s = Get-ProtoState $w
    $lines += "$w=$s"
    if ($s -eq 'enabled') { $bad++ }
}
$t12 = Get-ProtoState 'TLS 1.2'
$lines += "TLS 1.2=$t12"
if ($t12 -eq 'disabled') { $bad++ }

$status = if ($bad -gt 0) { 'WARN' } else { 'PASS' }
$detail = ($lines -join ' ; ')
if ($bad -gt 0) { $detail += ' - protocolli deboli abilitati o TLS1.2 disabilitato' }

[pscustomobject]@{ Name = '18_tls_protocols'; Status = $status; Detail = $detail }
