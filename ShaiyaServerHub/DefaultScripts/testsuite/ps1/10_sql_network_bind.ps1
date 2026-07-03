# 10_sql_network_bind.ps1
# Verifica su quali indirizzi il SQL (porta 1433) e' in ascolto.
# PASS  : solo indirizzi ammessi ($Ctx.AllowedAddresses).
# WARN  : in ascolto su 0.0.0.0 o :: (tutte le interfacce) -> restringere / riavviare SQL.
# FAIL  : niente in ascolto su 1433 (servizio fermo?).
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$allowed = if ($Ctx.AllowedAddresses) { $Ctx.AllowedAddresses } else { @('127.0.0.1','::1') }

$listen = @(Get-NetTCPConnection -State Listen -LocalPort 1433 |
            Select-Object -ExpandProperty LocalAddress -Unique)
$svc = Get-Process sqlservr | Select-Object -First 1

$wildcard = ($listen -contains '0.0.0.0') -or ($listen -contains '::')
$extra    = @($listen | Where-Object { $_ -notin $allowed })

if (-not $listen)      { $status = 'FAIL' }
elseif ($wildcard)     { $status = 'WARN' }
elseif ($extra.Count)  { $status = 'WARN' }
else                   { $status = 'PASS' }

$detail = "listen=[$($listen -join ', ')] ; ammessi=[$($allowed -join ', ')]"
if ($svc) { $detail += " ; sqlservr_start=$($svc.StartTime)" }
if ($wildcard) { $detail += " ; ATTENZIONE: bind su tutte le interfacce (riavvia MSSQLSERVER dopo la modifica in Config Manager)" }

[pscustomobject]@{ Name = '10_sql_network_bind'; Status = $status; Detail = $detail }
