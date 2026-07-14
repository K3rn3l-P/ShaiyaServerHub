# -------------------------------
# Monitoraggio e Ottimizzazione RAM
# -------------------------------

# 0. Bypass ExecutionPolicy temporaneo
$origPolicy = Get-ExecutionPolicy
Set-ExecutionPolicy Bypass -Scope Process -Force

# 1. Percorsi e logging
$scriptPath  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$logFolder   = Join-Path $scriptPath 'log'
$logFile     = Join-Path $logFolder  'MonitoraggioRisorse.log'
if (-not (Test-Path $logFolder)) { New-Item $logFolder -ItemType Directory | Out-Null }

function Log {
    param($msg)
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    Add-Content -Path $logFile -Value "$ts - $msg"
}

# 2. Lista processi da non terminare
$important = @(
    'ps_userLog','ps_session','ps_login','ps_gameLog','ps_game',
    'ps_dbAgent','PSM_AgentServer','PSM_AgentClient',
    'sqlservr','sqlwriter','sqlagent',
    'fdlauncher','ReportingServicesService','ssas','ssrs','ssis',
    'svchost','Tailscale','BalloonService','MsMpEng',
    'Apache2.4','WireGuardManager','WireGuardTunnel$WireGuardShaiya',
    'cheatengine-x86_64'
)

# 3. Controllo utilizzo RAM
$os        = Get-CimInstance Win32_OperatingSystem
$usedPct   = (1 - ($os.FreePhysicalMemory / $os.TotalVisibleMemorySize)) * 100
$threshold = 80

if ($usedPct -gt $threshold) {
    Log "RAM al $([math]::Round($usedPct,2))% > $threshold%: avvio ottimizzazione"

    # 4. Kill dei processi heavy (>200MB) non vitali
    $limitMB = 200
    Get-Process |
      Where-Object {
        ($_.WorkingSet64 / 1MB) -gt $limitMB -and
        $important -notcontains $_.Name
      } |
      ForEach-Object {
        try {
            Stop-Process -Id $_.Id -Force
            Log "Killed heavy process $($_.Name) (PID: $($_.Id)) - $([math]::Round($_.WorkingSet64/1MB,1)) MB"
        } catch {
            Log "Error stopping $($_.Name) (PID: $($_.Id)): $_"
        }
      }

# 6. Trim working set dei processi vitali (solo se hanno un handle valido e non sono sessione 0)
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Win32 {
  [DllImport("psapi.dll")]
  public static extern bool EmptyWorkingSet(IntPtr hProcess);
}
"@

Get-Process |
  Where-Object {
    $important -contains $_.Name -and
    $_.HandleCount -gt 0         -and  # ha handle validi
    $_.SessionId -ne 0           -and  # non e' kernel/sessione di sistema
    $_.Path                       # ha un eseguibile (skip di processi senza Path)
  } |
  ForEach-Object {
    try {
      [Win32]::EmptyWorkingSet($_.Handle) | Out-Null
      Log "Trim WS $($_.Name) (PID: $($_.Id))"
    }
    catch {
      # silenzia errori di accesso o handle invalido
    }
  }
    # 6. Svuota Standby List con EmptyStandbyList.exe
    $esl = Join-Path $scriptPath 'EmptyStandbyList.exe'
    if (Test-Path $esl) {
        & $esl standbylist            | Out-Null; Log "StandbyList cleared"
        & $esl workingsets            | Out-Null; Log "Workingsets cleared"
        & $esl priority0standbylist   | Out-Null; Log "Priority0 StandbyList cleared"
    } else {
        Log "EmptyStandbyList.exe non trovato in $scriptPath"
    }

    # 7. Riavvio SysMain (Superfetch)
    try {
        $svc = Get-Service SysMain -ErrorAction Stop
        if ($svc.Status -eq 'Running') {
            Stop-Service SysMain -Force
            Start-Sleep -Seconds 5
            Start-Service SysMain
            Log "SysMain riavviato"
        }
    } catch {
        Log "SysMain service error: $_"
    }

    # 8. Disabilita temporaneamente Memory Compression
    try {
        Disable-MMAgent -MemoryCompression
        Log "Memory Compression disabilitata"
        # (riabilitare se desiderato con: Enable-MMAgent -MemoryCompression)
    } catch {
        Log "MMAgent error: $_"
    }

    # 9. Garbage Collection di PowerShell
    [System.GC]::Collect()
    Log "Garbage Collection eseguita"

    # 10. Pulizia variabili script
    Get-Variable |
      Where-Object { $_.Options -eq 'None' } |
      ForEach-Object { Clear-Variable -Name $_.Name -ErrorAction SilentlyContinue }
    Log "Variabili script cancellate"

    # 11. Chiudi sessioni RDP inattive
    qwinsta /server:localhost |
      Where-Object { $_ -match 'Disc' } |
      ForEach-Object {
        $sid = ($_ -split '\s+')[2]
        logoff $sid /server:localhost
        Log "Logged off RDP session $sid"
      }

    Log "Ottimizzazione completata."
} else {
    Log "RAM al $([math]::Round($usedPct,2))%, entro soglia."
}

# 12. Ripristino ExecutionPolicy
Set-ExecutionPolicy $origPolicy -Scope Process -Force
