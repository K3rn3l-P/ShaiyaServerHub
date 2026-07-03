# 13_login_auditing.ps1
# Microsoft best practice: registrare almeno i login FALLITI (AuditLevel 2=Failure o 3=Both).
# WARN se i failed login non vengono registrati.
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$map = @{ 0 = 'None'; 1 = 'Success'; 2 = 'Failure'; 3 = 'Both' }

$inst = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').'MSSQLSERVER'
if (-not $inst) {
    [pscustomobject]@{ Name = '13_login_auditing'; Status = 'WARN'; Detail = 'istanza MSSQLSERVER non trovata nel registry' }
    return
}

$key = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$inst\MSSQLServer"
$al  = (Get-ItemProperty $key).'AuditLevel'
$alInt = [int]$al
$detail = "AuditLevel=$alInt ($($map[$alInt]))"

if ($alInt -eq 2 -or $alInt -eq 3) {
    $status = 'PASS'
} else {
    $status = 'WARN'
    $detail += ' - i failed login NON vengono registrati (imposta Failed/Both)'
}

[pscustomobject]@{ Name = '13_login_auditing'; Status = $status; Detail = $detail }
