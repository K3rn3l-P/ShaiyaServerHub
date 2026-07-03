# 20_os_patch_smb.ps1
# CIS Windows + KEV: SMBv1 disabilitato + patch OS recenti.
# WARN se SMBv1 attivo o se l'ultimo hotfix e' piu' vecchio di $Ctx.WinUpdateMaxAgeDays (default 60).
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$maxAge = if ($Ctx.WinUpdateMaxAgeDays) { [int]$Ctx.WinUpdateMaxAgeDays } else { 60 }

$smb1 = (Get-SmbServerConfiguration).EnableSMB1Protocol
$last = (Get-HotFix | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn -Descending |
         Select-Object -First 1).InstalledOn
$age  = if ($last) { (New-TimeSpan -Start $last -End (Get-Date)).Days } else { $null }

$lastStr = if ($last) { $last.ToString('yyyy-MM-dd') + " ($age g)" } else { 'sconosciuto' }
$detail = "SMBv1=$smb1 ; ultimo_hotfix=$lastStr (soglia $maxAge g)"
$status = 'PASS'

if ($smb1) { $status = 'WARN'; $detail += ' - SMBv1 ATTIVO (disabilitare)' }
if (($null -eq $age) -or ($age -gt $maxAge)) {
    if ($status -eq 'PASS') { $status = 'WARN' }
    $detail += ' - patch oltre soglia/sconosciute'
}

[pscustomobject]@{ Name = '20_os_patch_smb'; Status = $status; Detail = $detail }
