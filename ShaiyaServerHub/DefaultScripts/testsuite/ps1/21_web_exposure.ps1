# 21_web_exposure.ps1
# Esposizione del web panel (xampp/Apache, web root C:\xampp\htdocs). Il sito reindirizza HTTP->HTTPS,
# quindi si testa via HTTPS con curl.exe -k (cert self-signed accettato). Eseguito solo se httpd e' attivo.
#   FAIL: .git, config worker (credenziali), script .ps1, endpoint debug, source PHP grezzo raggiungibili (200).
#   WARN: directory listing, adm-panel senza auth, header di sicurezza mancanti sulla home.
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$base = if ($Ctx.WebBaseUrl) { $Ctx.WebBaseUrl } else { 'https://127.0.0.1' }
$base = $base.TrimEnd('/')

if (-not (Get-Process httpd -ErrorAction SilentlyContinue)) {
    [pscustomobject]@{ Name = '21_web_exposure'; Status = 'PASS'; Detail = 'httpd non in esecuzione - test web saltato' }
    return
}
$curl = (Get-Command curl.exe -ErrorAction SilentlyContinue).Source
if (-not $curl) {
    [pscustomobject]@{ Name = '21_web_exposure'; Status = 'WARN'; Detail = 'curl.exe non disponibile - test web saltato' }
    return
}

function Get-Code([string]$p) { & $curl -sk -o NUL -w "%{http_code}" --max-time 8 ($base + $p) }
function Get-Body([string]$p) { & $curl -sk --max-time 8 ($base + $p) }

$fail = @()
$warn = @()

$critical = [ordered]@{
    '/.git/config'                                        = 'git repo esposto'
    '/tools/task-scheduler-worker/worker.config.json'     = 'config worker (credenziali)'
    '/tools/security-monitor/security-audit-advanced.ps1' = 'script .ps1 servito'
    '/Updater/api/test_sah_key.php'                       = 'endpoint debug'
}
foreach ($p in $critical.Keys) {
    if ((Get-Code $p) -eq '200') { $fail += ($p + ' (' + $critical[$p] + ')') }
}
foreach ($p in '/db.php', '/db_pdo.php', '/config.php') {
    if ((Get-Code $p) -eq '200' -and ((Get-Body $p) -match '<\?php')) { $fail += ($p + ' (source PHP grezzo)') }
}
foreach ($p in '/sql/', '/tools/', '/config/', '/uploads/', '/inc/', '/modules/') {
    if ((Get-Code $p) -eq '200' -and ((Get-Body $p) -match 'Index of')) { $warn += ($p + ' (directory listing)') }
}
if ((Get-Code '/adm-panel/') -eq '200') { $warn += '/adm-panel/ raggiungibile (atteso 401/403/redirect)' }

$hdrText = (& $curl -sk -o NUL -D - --max-time 8 ($base + '/')) -join "`n"
foreach ($h in 'X-Frame-Options', 'Content-Security-Policy', 'X-Content-Type-Options') {
    if ($hdrText -notmatch ('(?im)^\s*' + [regex]::Escape($h) + '\s*:')) { $warn += "header mancante: $h" }
}

$status = if ($fail.Count) { 'FAIL' } elseif ($warn.Count) { 'WARN' } else { 'PASS' }
$detail = if ($status -eq 'PASS') { 'nessuna esposizione web rilevata (HTTPS)' }
          else { "FAIL=[$($fail -join '; ')] WARN=[$($warn -join '; ')]" }

[pscustomobject]@{ Name = '21_web_exposure'; Status = $status; Detail = $detail }
