using System;
using System.IO;

namespace ShaiyaServerHub.Core.Ai.Sensors
{
    /// <summary>
    /// Legge il SQL Server Audit nativo (server audit "ShaiyaHub_LoginAudit"/"ShaiyaHub_SchemaChangesAudit",
    /// installati/verificati da <c>testsuite\sql\23_sql_audit_install.sql</c>, TO APPLICATION_LOG) —
    /// "config-drift" per il database, stesso principio di <see cref="ExternalToolsManager"/>/autorunsc
    /// ma per schema SQL invece che autostart Windows.
    ///
    /// Verificato sul server reale PRIMA di scrivere questo file (non ipotesi): entrambi gli audit
    /// scrivono con EventID 33205, provider "MSSQLSERVER", sul log Application — differenziati dal campo
    /// <c>action_id</c> nel testo del messaggio: CR/AL/DR = modifica schema (CREATE/ALTER/DROP), LGIF =
    /// login fallito. Un login fallito reale (`ojkndsaouin` con password sbagliata) e una modifica schema
    /// reale (CREATE+DROP di una tabella di test) sono stati generati e ispezionati a mano per confermare
    /// il formato — non un formato assunto dalla documentazione.
    /// </summary>
    public static class SqlAuditSensor
    {
        private static string LastCheckMarker => Path.Combine(AiPaths.Root, "sqlaudit-lastcheck.txt");

        /// <summary>Modifiche di schema (CREATE/ALTER/DROP) negli ultimi N minuti — per Test Suite/tool AI on-demand.</summary>
        public static int SchemaChanges(int minutes, out string output) =>
            QuerySchemaChanges(DateTime.UtcNow.AddMinutes(-Math.Max(1, minutes)), out output);

        /// <summary>Login SQL falliti (a livello SERVER, qualsiasi login) negli ultimi N minuti, raggruppati per IP.</summary>
        public static int FailedSqlLogins(int minutes, out string output) =>
            QueryFailedLogins(DateTime.UtcNow.AddMinutes(-Math.Max(1, minutes)), out output);

        /// <summary>
        /// Per il digest periodico (<see cref="SecurityDigestScheduler"/>): "coda" gli eventi NUOVI dal
        /// controllo precedente (marker persistito su file, sopravvive a riavvio Hub) invece di una
        /// finestra fissa — un evento non viene MAI segnalato due volte, a differenza di una finestra
        /// scorrevole che lo riproporrebbe ad ogni tick finché non esce dalla finestra. Primo avvio
        /// (marker assente): bootstrap silenzioso (marker=ora, nessun finding) — non dumpare lo storico
        /// pregresso al primo giro dopo l'attivazione.
        /// </summary>
        public static void TailNewFindings(HubSettings settings, System.Collections.Generic.List<Finding> findings)
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime sinceUtc;
            bool bootstrap = !File.Exists(LastCheckMarker);
            if (bootstrap)
            {
                sinceUtc = nowUtc;
            }
            else if (!DateTime.TryParse(File.ReadAllText(LastCheckMarker), null,
                     System.Globalization.DateTimeStyles.RoundtripKind, out sinceUtc))
            {
                sinceUtc = nowUtc.AddMinutes(-15); // marker corrotto: fallback prudente, meglio un piccolo overlap che perdere eventi
            }

            try { Directory.CreateDirectory(AiPaths.Root); File.WriteAllText(LastCheckMarker, nowUtc.ToString("o")); }
            catch (Exception ex) { App.WriteLog("AI-SQLAUDIT", "errore scrittura marker: " + ex.Message); }

            if (bootstrap) return; // nessun confronto ancora possibile, stesso spirito del baseline autoruns

            if (QuerySchemaChanges(sinceUtc, out var schemaOut) == 0 && !schemaOut.Contains("NESSUNA_MODIFICA_SCHEMA"))
            {
                foreach (var line in schemaOut.Replace("\r", "").Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var f = line.Split('\t');
                    if (f.Length >= 6 && f[0] == "SCHEMA")
                        findings.Add(new Finding(FindingSeverity.High, "sql-schema",
                            $"modifica schema DB: {f[3]} da '{f[2]}' su {f[4]} — {f[5]}"));
                }
            }

            if (QueryFailedLogins(sinceUtc, out var loginOut) == 0 && !loginOut.Contains("NESSUN_LOGIN_FALLITO"))
            {
                int maxCount = 0; string maxIp = null;
                foreach (var line in loginOut.Replace("\r", "").Split('\n'))
                {
                    var f = line.Split('\t');
                    if (f.Length >= 4 && f[0] == "IP" && int.TryParse(f[2], out int n) && n > maxCount) { maxCount = n; maxIp = f[1]; }
                }
                if (maxCount >= settings.AiDigestSqlFailedLoginMedium)
                    findings.Add(new Finding(maxCount >= settings.AiDigestSqlFailedLoginHigh ? FindingSeverity.High : FindingSeverity.Medium,
                        "sql-login", $"login SQL falliti: fino a {maxCount} tentativi da '{maxIp}'"));
            }
        }

        private static int QuerySchemaChanges(DateTime sinceUtc, out string output)
        {
            string since = sinceUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss");
            string ps = $@"
$ErrorActionPreference='SilentlyContinue'
$since = [datetime]'{since}'
$events = Get-WinEvent -FilterHashtable @{{LogName='Application';ProviderName='MSSQLSERVER';Id=33205;StartTime=$since}} -MaxEvents 500 -ErrorAction SilentlyContinue
if (-not $events) {{ 'NESSUNA_MODIFICA_SCHEMA' }}
else {{
  foreach ($e in $events) {{
    $map = @{{}}
    foreach ($l in ($e.Message -split ""`n"")) {{ if ($l -match '^([a-z_]+):(.*)$') {{ $map[$matches[1]] = $matches[2].Trim() }} }}
    $aid = $map['action_id']
    if ($aid -eq 'CR' -or $aid -eq 'AL' -or $aid -eq 'DR') {{
      'SCHEMA'+[char]9+$e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss')+[char]9+$map['server_principal_name']+[char]9+$aid+[char]9+($map['database_name']+'.'+$map['schema_name']+'.'+$map['object_name'])+[char]9+$map['statement']
    }}
  }}
}}";
            return ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
        }

        private static int QueryFailedLogins(DateTime sinceUtc, out string output)
        {
            string since = sinceUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss");
            string ps = $@"
$ErrorActionPreference='SilentlyContinue'
$since = [datetime]'{since}'
$events = Get-WinEvent -FilterHashtable @{{LogName='Application';ProviderName='MSSQLSERVER';Id=33205;StartTime=$since}} -MaxEvents 1000 -ErrorAction SilentlyContinue
$rows = foreach ($e in $events) {{
  $map = @{{}}
  foreach ($l in ($e.Message -split ""`n"")) {{ if ($l -match '^([a-z_]+):(.*)$') {{ $map[$matches[1]] = $matches[2].Trim() }} }}
  if ($map['action_id'] -eq 'LGIF') {{ [pscustomobject]@{{ Ip=$map['client_ip']; User=$map['server_principal_name'] }} }}
}}
if (-not $rows) {{ 'NESSUN_LOGIN_FALLITO' }}
else {{
  $rows | Group-Object Ip | Sort-Object Count -Descending | ForEach-Object {{
    $users = ($_.Group | Select-Object -ExpandProperty User -Unique) -join ','
    'IP'+[char]9+$_.Name+[char]9+$_.Count+[char]9+$users
  }}
}}";
            return ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
        }
    }
}
