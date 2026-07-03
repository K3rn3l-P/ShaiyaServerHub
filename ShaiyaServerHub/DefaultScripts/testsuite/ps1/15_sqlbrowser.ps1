# 15_sqlbrowser.ps1
# Con porta statica (1433) il servizio SQL Server Browser non serve. Se in esecuzione facilita
# l'enumerazione delle istanze: WARN. PASS se fermo/disabilitato/assente.
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$svc = Get-Service SQLBrowser

if (-not $svc) {
    $status = 'PASS'; $detail = 'SQLBrowser non installato'
} elseif ($svc.Status -eq 'Running') {
    $status = 'WARN'; $detail = 'SQLBrowser in esecuzione - con porta statica 1433 e'' superfluo (disabilitalo per ridurre enumerazione)'
} else {
    $status = 'PASS'; $detail = "SQLBrowser $($svc.Status)"
}

[pscustomobject]@{ Name = '15_sqlbrowser'; Status = $status; Detail = $detail }
