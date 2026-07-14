# -------------------------------
# 4. AUTO-RAM-OPTIMIZER.PS1 (v2)
# -------------------------------

# 0. Bypass temporaneo ExecutionPolicy
$origPolicy = Get-ExecutionPolicy
Set-ExecutionPolicy Bypass -Scope Process -Force

# 1. Lock per evitare esecuzioni sovrapposte
$lockFile = "$env:TEMP\AutoRamOptimizer.lock"
if (Test-Path $lockFile) { return }
New-Item -Path $lockFile -ItemType File | Out-Null

# 2. Percorsi e logging
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$logFolder  = Join-Path  $scriptPath 'log'
$logFile    = Join-Path  $logFolder  'MonitoraggioRisorse.log'
if (-not (Test-Path $logFolder)) { New-Item $logFolder -ItemType Directory | Out-Null }
function Log { param($m) Add-Content $logFile ("{0:yyyy-MM-dd HH:mm:ss} - {1}" -f (Get-Date), $m) }

# 3. Whitelist di tutti i processi critici di sistema e GUI
$important = @(
  'ps_userLog','ps_session','ps_login','ps_gameLog','ps_game','ps_dbAgent',
  'PSM_AgentServer','PSM_AgentClient','sqlservr','sqlwriter','sqlagent',
  'fdlauncher','ReportingServicesService','ssas','ssrs','ssis','svchost',
  'explorer','conhost','csrss','wininit','winlogon','services','lsass',
  'svchost','dwm','Tailscale','BalloonService','MsMpEng',
  'Apache2.4','WireGuardManager','WireGuardTunnel$WireGuardShaiya',
  'cheatengine-x86_64', 'Code', 'Visual Studio Code'
)

# 4. Controllo utilizzo RAM
$os      = Get-CimInstance Win32_OperatingSystem
$usedPct = (1 - ($os.FreePhysicalMemory / $os.TotalVisibleMemorySize)) * 100
$threshold = 50

if ($usedPct -gt $threshold) {
  Log "RAM al $([math]::Round($usedPct,2))% > $threshold%: inizio ottimizzazione"

  # 5. Kill solo heavy process (>200MB) non vitali
  $limitMB = 200
  Get-Process |
    Where-Object {
      ($_.WorkingSet64/1MB) -gt $limitMB -and
      $important -notcontains $_.Name
    } | ForEach-Object {
      try {
        Stop-Process -Id $_.Id -Force
        Log "Killed heavy process $($_.Name) (PID: $($_.Id)) - $([math]::Round($_.WorkingSet64/1MB,1)) MB"
      }
      catch {
        Log "Errore killing $($_.Name) (PID: $($_.Id)): $_"
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

  # 7. Svuota solo priority0 standby list
  $esl = Join-Path $scriptPath 'EmptyStandbyList.exe'
  if (Test-Path $esl) {
    & $esl priority0standbylist | Out-Null
    Log "Priority0 StandbyList cleared"
  } else {
    Log "EmptyStandbyList.exe non trovato in $scriptPath"
  }

  # 8. Riavvio SysMain solo se inattivo da >1h
  try {
    $svc = Get-Service SysMain -ErrorAction Stop
    if ($svc.Status -eq 'Running' -and (Get-Date) -gt ($svc.LastStartTime.AddHours(1))) {
      Stop-Service SysMain -Force; Start-Sleep 5; Start-Service SysMain
      Log "SysMain riavviato (idle >1h)"
    }
  } catch {
    Log "SysMain error: $_"
  }

  # 9. Toggle rapido Memory Compression
  try {
    Disable-MMAgent -MemoryCompression
    Enable-MMAgent  -MemoryCompression
    Log "Memory Compression toggled"
  } catch {
    Log "Error toggling MemoryCompression: $_"
  }

  # 10. Garbage Collection
  [System.GC]::Collect()
  Start-Sleep -Milliseconds 100
  Log "Garbage Collection eseguita"

  # 11. Disconnessione RDP inattive (se presenti)
  #qwinsta /server:localhost |
  #  Where-Object { $_ -match 'Disc' } |
  #  ForEach-Object {
  #    $sid = ($_ -split '\s+')[2]
  #    logoff $sid /server:localhost
  #    Log "Logged off RDP session $sid"
  #  }

  Log "Ottimizzazione completata."
}
else {
  Log "RAM al $([math]::Round($usedPct,2))%, entro soglia."
}

# 12. Cleanup
Remove-Item $lockFile -Force
Set-ExecutionPolicy $origPolicy -Scope Process -Force
