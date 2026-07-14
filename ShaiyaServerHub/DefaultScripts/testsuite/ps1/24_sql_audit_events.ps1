# 24_sql_audit_events.ps1
# Legge il SQL Server Audit nativo (EventID 33205, provider MSSQLSERVER, Application log) installato da
# 23_sql_audit_install.sql - segnala modifiche di schema DB (config-drift) e login SQL falliti a livello
# server nelle ultime 24 ore. Punto-in-tempo (badge Test Suite), diverso dal digest automatico che usa
# un marker "dall'ultimo controllo" (vedi Core\Ai\Sensors\SqlAuditSensor.cs) per non ri-segnalare due
# volte lo stesso evento - qui invece ad ogni run mostra sempre la finestra fissa piu' recente.
# NOTA: nessun carattere non-ASCII in questo file, vedi 22_autoruns_baseline.ps1 per il motivo (bug
# reale: em-dash in una stringa rompe il parser PowerShell 5.1 su file UTF-8 senza BOM).
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$since = (Get-Date).AddHours(-24)

$events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'MSSQLSERVER'; Id = 33205; StartTime = $since } -MaxEvents 1000 -ErrorAction SilentlyContinue

if (-not $events) {
    [pscustomobject]@{ Name = '24_sql_audit_events'; Status = 'INFO'; Detail = 'nessun evento di audit trovato - il SQL Server Audit e'' installato? Esegui 23_sql_audit_install.sql.' }
    return
}

$schemaChanges = @()
$loginFailures = @()

foreach ($e in $events) {
    $map = @{}
    foreach ($l in ($e.Message -split "`n")) {
        if ($l -match '^([a-z_]+):(.*)$') { $map[$matches[1]] = $matches[2].Trim() }
    }
    $aid = $map['action_id']
    if ($aid -eq 'CR' -or $aid -eq 'AL' -or $aid -eq 'DR') {
        $schemaChanges += "$($e.TimeCreated.ToString('MM-dd HH:mm')) $($map['server_principal_name']) $aid $($map['database_name']).$($map['schema_name']).$($map['object_name'])"
    }
    elseif ($aid -eq 'LGIF') {
        $loginFailures += [pscustomobject]@{ Ip = $map['client_ip']; User = $map['server_principal_name'] }
    }
}

$parts = @()
$status = 'PASS'

if ($schemaChanges.Count -gt 0) {
    $status = 'WARN'
    $sample = ($schemaChanges | Select-Object -First 5) -join ' || '
    $parts += "$($schemaChanges.Count) modifica/che schema: $sample"
}

if ($loginFailures.Count -gt 0) {
    $byIp = $loginFailures | Group-Object Ip | Sort-Object Count -Descending
    $top = $byIp | Select-Object -First 1
    if ($top.Count -ge 5) {
        $status = 'WARN'
        $parts += "login SQL falliti: $($loginFailures.Count) totali, fino a $($top.Count) da '$($top.Name)'"
    } else {
        $parts += "login SQL falliti: $($loginFailures.Count) totali (sotto soglia)"
    }
}

if ($parts.Count -eq 0) { $parts += 'nessuna modifica schema, nessun login SQL fallito rilevante (ultime 24h)' }

[pscustomobject]@{ Name = '24_sql_audit_events'; Status = $status; Detail = ($parts -join ' ; ') }
