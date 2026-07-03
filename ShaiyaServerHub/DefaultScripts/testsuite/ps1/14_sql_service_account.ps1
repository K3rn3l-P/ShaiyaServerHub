# 14_sql_service_account.ps1
# CIS: il servizio SQL non deve girare come LocalSystem / Administrator (privilegio eccessivo a livello OS).
# Ideale: virtual account 'NT SERVICE\MSSQLSERVER' o un account di servizio dedicato a basso privilegio.
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$svc = Get-CimInstance Win32_Service -Filter "Name='MSSQLSERVER'"

if (-not $svc) {
    [pscustomobject]@{ Name = '14_sql_service_account'; Status = 'WARN'; Detail = 'servizio MSSQLSERVER non trovato' }
    return
}

$acct = $svc.StartName
$bad = ($acct -in @('LocalSystem', 'NT AUTHORITY\SYSTEM')) -or ($acct -like '*\Administrator')
$detail = "service_account=$acct"
if ($bad) {
    $status = 'WARN'
    $detail += ' - privilegio eccessivo: usare virtual account NT SERVICE\MSSQLSERVER o account dedicato'
} else {
    $status = 'PASS'
}

[pscustomobject]@{ Name = '14_sql_service_account'; Status = $status; Detail = $detail }
