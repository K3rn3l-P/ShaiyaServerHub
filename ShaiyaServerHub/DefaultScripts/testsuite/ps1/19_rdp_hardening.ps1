# 19_rdp_hardening.ps1
# CIS/Microsoft: se RDP e' abilitato, deve usare NLA (UserAuthentication=1) e SecurityLayer=TLS (2)
# per evitare attacchi classe BlueKeep. WARN se NLA off / SecurityLayer non TLS.
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$k = 'HKLM:\System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp'
$nla   = [int](Get-ItemProperty $k).UserAuthentication
$sl    = [int](Get-ItemProperty $k).SecurityLayer
$fDeny = [int](Get-ItemProperty 'HKLM:\System\CurrentControlSet\Control\Terminal Server').fDenyTSConnections

$detail = "NLA=$nla ; SecurityLayer=$sl ; RDP_disabilitato=$fDeny"
$status = 'PASS'

if ($fDeny -eq 0) {
    if ($nla -ne 1) { $status = 'WARN'; $detail += ' - NLA disattivo (classe BlueKeep)' }
    elseif ($sl -lt 2) { $status = 'WARN'; $detail += ' - SecurityLayer non TLS' }
} else {
    $detail += ' (RDP disabilitato)'
}

[pscustomobject]@{ Name = '19_rdp_hardening'; Status = $status; Detail = $detail }
