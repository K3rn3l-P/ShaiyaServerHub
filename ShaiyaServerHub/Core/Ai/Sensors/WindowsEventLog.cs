using System;
using System.Collections.Generic;
using System.Linq;

namespace ShaiyaServerHub.Core.Ai.Sensors
{
    /// <summary>
    /// Sensori Event Log Windows — riusa ESATTAMENTE il pattern Get-WinEvent + [xml]ToXml() già in
    /// <see cref="EventTriggerBuilder"/> (finora usato solo per il trigger 7036 dei task pianificati).
    /// Verificato sul server reale (non ipotesi) prima di scrivere questo file:
    /// - I 4625 (login falliti) osservati sul server sono TUTTI rumore interno (MSSQLSERVER/Kerberos,
    ///   IpAddress="-", nessun IP reale su 31 eventi storici) — <see cref="FailedLogons"/> filtra via
    ///   IpAddress mancante/loopback per non trasformare rumore interno in falsi allarmi di brute force.
    /// - I 7031/7034 (crash servizio) usano `param1` = DisplayName del servizio, stesso schema del 7036
    ///   già letto da EventTriggerBuilder — confermato con un crash reale (ps_login, "Shaiya Login Server").
    /// - Il 41 (Kernel-Power) porta l'informazione utile in `.Message`, non nei campi EventData strutturati.
    /// </summary>
    public static class WindowsEventLog
    {
        /// <summary>
        /// Login falliti (Security 4625) + account lockout (4740) con IP remoto REALE (esclude "-"/vuoto/
        /// loopback, cioè il rumore interno verificato sul server). Raggruppa per IP, ordina per conteggio.
        /// </summary>
        public static int FailedLogons(int minutes, out string output)
        {
            string ps = $@"
$ErrorActionPreference='SilentlyContinue'
$since = (Get-Date).AddMinutes(-{minutes})
$rows = foreach($id in 4625,4740) {{
  Get-WinEvent -FilterHashtable @{{LogName='Security';Id=$id;StartTime=$since}} -MaxEvents 500 -ErrorAction SilentlyContinue |
    ForEach-Object {{
      $d = ([xml]$_.ToXml()).Event.EventData.Data
      $ip = ($d | Where-Object {{ $_.Name -eq 'IpAddress' }}).'#text'
      $user = ($d | Where-Object {{ $_.Name -eq 'TargetUserName' }}).'#text'
      $lt = ($d | Where-Object {{ $_.Name -eq 'LogonType' }}).'#text'
      if ($ip -and $ip -ne '-' -and $ip -ne '127.0.0.1' -and $ip -ne '::1') {{
        [pscustomobject]@{{ Id=$_.Id; Ip=$ip; User=$user; LogonType=$lt }}
      }}
    }}
}}
if (-not $rows) {{ 'NESSUN_EVENTO_CON_IP_REALE' }}
else {{
  $rows | Group-Object Ip | Sort-Object Count -Descending | ForEach-Object {{
    $types = ($_.Group | Select-Object -ExpandProperty LogonType -Unique) -join ','
    $users = ($_.Group | Select-Object -ExpandProperty User -Unique | Where-Object {{ $_ }}) -join ','
    'IP'+[char]9+$_.Name+[char]9+$_.Count+[char]9+$types+[char]9+$users
  }}
}}";
            int rc = ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
            return rc;
        }

        /// <summary>
        /// Riavvii/crash inattesi (System 6008, 41, 1001) nella finestra, CON i 1074 (shutdown/restart
        /// pianificato, User32) vicini come discriminante — un 41/6008 senza un 1074 nella stessa finestra
        /// è uno scenario "il server è ripartito da solo mentre ero via", non una manutenzione voluta.
        /// Filtro NOTO applicato: sul server reale i 41 con messaggio "Hypervisor launch failed..." sono
        /// un artefatto BENIGNO di virtualizzazione annidata (verificato: 3 occorrenze storiche, stesso
        /// messaggio, nessun disservizio correlato) — esclusi per non generare falsi allarmi ad ogni
        /// giro, stesso principio del filtro RANGE_HACK_PVE in <see cref="SecurityTelemetry"/>.
        /// </summary>
        public static int UnexpectedRestarts(int minutes, out string output)
        {
            string ps = $@"
$ErrorActionPreference='SilentlyContinue'
$since = (Get-Date).AddMinutes(-{minutes})
$crashes = foreach($id in 6008,41,1001) {{
  Get-WinEvent -FilterHashtable @{{LogName='System';Id=$id;StartTime=$since}} -MaxEvents 50 -ErrorAction SilentlyContinue
}}
$crashes = $crashes | Where-Object {{ $_.Message -notmatch 'Hypervisor launch failed' }}
$planned = Get-WinEvent -FilterHashtable @{{LogName='System';ProviderName='User32';Id=1074;StartTime=$since}} -MaxEvents 50 -ErrorAction SilentlyContinue
if (-not $crashes) {{ 'NESSUN_RIAVVIO_INATTESO' }}
else {{
  $crashes | Sort-Object TimeCreated -Descending | ForEach-Object {{
    $c = $_
    $hasPlanned = $planned | Where-Object {{ [Math]::Abs(($_.TimeCreated - $c.TimeCreated).TotalMinutes) -lt 10 }}
    $tag = if ($hasPlanned) {{ 'vicino-a-1074(pianificato)' }} else {{ 'NESSUN-1074-VICINO' }}
    $msg = ($c.Message -replace ""`r`n"",' ')
    if ($msg.Length -gt 120) {{ $msg = $msg.Substring(0,120) }}
    'EVT'+[char]9+$c.Id+[char]9+$c.TimeCreated.ToString('yyyy-MM-dd HH:mm')+[char]9+$tag+[char]9+$msg
  }}
}}";
            int rc = ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
            return rc;
        }

        /// <summary>
        /// Crash di servizio (System 7031/7034/7024, provider Service Control Manager) — DISTINTI dal
        /// 7036 (cambio stato normale, già letto da EventTriggerBuilder). param1 = DisplayName servizio
        /// (stesso schema del 7036, verificato su un crash reale).
        /// </summary>
        public static int ServiceCrashes(int minutes, out string output)
        {
            string ps = $@"
$ErrorActionPreference='SilentlyContinue'
$since = (Get-Date).AddMinutes(-{minutes})
$rows = foreach($id in 7031,7034,7024) {{
  Get-WinEvent -FilterHashtable @{{LogName='System';ProviderName='Service Control Manager';Id=$id;StartTime=$since}} -MaxEvents 100 -ErrorAction SilentlyContinue |
    ForEach-Object {{
      $svc = (([xml]$_.ToXml()).Event.EventData.Data | Where-Object {{ $_.Name -eq 'param1' }}).'#text'
      'SVC'+[char]9+$_.Id+[char]9+$_.TimeCreated.ToString('yyyy-MM-dd HH:mm')+[char]9+$svc
    }}
}}
if (-not $rows) {{ 'NESSUN_CRASH_SERVIZIO' }} else {{ $rows }}";
            int rc = ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
            return rc;
        }
    }
}
