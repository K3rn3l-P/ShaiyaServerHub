# 12_services_health.ps1
# Stato di salute dei servizi: MSSQLSERVER in esecuzione + presenza dei processi ps_*.
# FAIL : MSSQLSERVER non Running.  WARN : SQL ok ma nessun processo ps_*.  PASS : entrambi ok.
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$parts = @()

$mssql = Get-Service MSSQLSERVER
$mssqlStatus = if ($mssql) { "$($mssql.Status)" } else { 'NOT FOUND' }
$parts += "MSSQLSERVER=$mssqlStatus"

$ps = @(Get-Process | Where-Object { $_.Name -like 'ps_*' } |
        Select-Object -ExpandProperty Name -Unique)
$parts += "ps_processes=[$($ps -join ', ')]"

if (-not $mssql -or $mssql.Status -ne 'Running') { $status = 'FAIL' }
elseif ($ps.Count -eq 0)                         { $status = 'WARN' }
else                                             { $status = 'PASS' }

[pscustomobject]@{ Name = '12_services_health'; Status = $status; Detail = ($parts -join ' ; ') }
