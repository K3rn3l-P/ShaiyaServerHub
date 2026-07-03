# 11_env_creds.ps1
# Verifica che le variabili d'ambiente con le credenziali DB ESISTANO (a livello Machine).
# NON stampa mai i valori: solo presente/assente. FAIL se ne manca una.
param([hashtable]$Ctx = @{})

$names = @('SHAIYA_LOGDB_USER','SHAIYA_LOGDB_PWD','DB_USER','DB_PASSWORD')
$present = @()
$missing = @()

foreach ($n in $names) {
    $v = [Environment]::GetEnvironmentVariable($n, 'Machine')
    if ([string]::IsNullOrEmpty($v)) { $missing += $n } else { $present += $n }
}

$status = if ($missing.Count -gt 0) { 'FAIL' } else { 'PASS' }
$detail = "present=[$($present -join ', ')] ; missing=[$($missing -join ', ')]"

[pscustomobject]@{ Name = '11_env_creds'; Status = $status; Detail = $detail }
