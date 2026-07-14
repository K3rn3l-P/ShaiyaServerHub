using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using ShaiyaServerHub.Core.Ai.Sensors;
using ShaiyaServerHub.Core.WebServer;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Sweep proattivo più ampio di <see cref="WatchLoop"/> (quello copre SOLO servizi di gioco fermi) —
    /// colma il gap "il Hub non controlla nulla da solo su Windows/SQL/web/sicurezza, serve che l'admin
    /// scriva in chat" (osservato in sessione, poi esteso su richiesta esplicita a "controlla TUTTO il
    /// server" — vedi PLAN §P4). Ogni 15 minuti: controlli ECONOMICI in C# puro (le STESSE classi usate
    /// dai tool T0 — <see cref="SecurityTelemetry"/>, <see cref="WindowsEventLog"/>,
    /// <see cref="DiskSpaceSensor"/>, <see cref="SqlHealthSensor"/>, <see cref="WebProbe"/>,
    /// <see cref="TestSuiteReport"/>, <see cref="TaskSchedulerService"/> — chiamate DIRETTAMENTE, zero
    /// costo LLM/OpenRouter). Ogni finding ha una GRAVITÀ deterministica (mai decisa dal modello, vedi
    /// <see cref="Finding"/>): solo se la gravità massima del giro raggiunge la soglia configurata
    /// (default Medium) scatta 1 richiesta <see cref="AgentBrain.Chat"/> + notifica Telegram. Un giro
    /// pulito o con SOLI finding Low resta silenzioso su Telegram (zero spam) ma finisce SEMPRE
    /// nell'audit log (`hub.log`) — soddisfa "voglio che controlli e riporti tutto" senza svegliare il
    /// telefono per rumore.
    ///
    /// Finestra di grazia 30 min dall'avvio del PROCESSO Hub (non del server): appena dopo un riavvio,
    /// servizi/DB/canale comandi possono ancora stare partendo — un giro troppo presto darebbe falsi
    /// positivi (richiesta esplicita dell'utente). Stesso motivo architetturale di WatchLoop per restare
    /// nel processo Hub invece che un CLI headless separato (vedi WatchLoop per il dettaglio).
    /// </summary>
    public class SecurityDigestScheduler : IDisposable
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(30);
        private static readonly Regex RowsAffected = new Regex(@"\((\d+)\s+rows? affected\)", RegexOptions.IgnoreCase);

        private readonly HubSettings _settings;
        /// <summary>Non readonly: se le Impostazioni cambiano, AiControl ricrea AgentBrain e aggiorna questo riferimento.</summary>
        public AgentBrain Brain;

        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly Timer _timer;
        private volatile bool _busy;

        public SecurityDigestScheduler(HubSettings settings, AgentBrain brain)
        {
            _settings = settings;
            Brain = brain;
            _timer = new Timer(Tick, null, SweepInterval, SweepInterval);
        }

        private void Tick(object state)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (!_settings.AiMasterEnabled || Invariants.KillSwitchActive || Brain == null) return;
                if (DateTime.UtcNow - _startedUtc < StartupGrace) return; // finestra di grazia avvio

                var findings = CollectFindings();
                if (findings.Count == 0) return; // tutto ok: zero costo LLM, zero log, zero notifica

                // Audit SEMPRE, qualunque gravità — questo è il "riporta tutto" (consultabile in hub.log
                // anche per i Low che non arrivano mai su Telegram).
                foreach (var f in findings.OrderByDescending(f => f.Severity))
                    App.WriteLog("AI-DIGEST", $"[{f.Severity}] {f.Category}: {f.Text}");

                var maxSev = findings.Max(f => f.Severity);
                var minNotifySev = ParseSeverity(_settings.AiDigestTelegramMinSeverity, FindingSeverity.Medium);
                if (maxSev < minNotifySev)
                {
                    // Sotto soglia: niente turno LLM (non vale la spesa di una richiesta OpenRouter per
                    // "indagare" un rumore di fondo), niente Telegram. Resta solo nell'audit log sopra.
                    OutcomeTracker.Record("security_digest", "LoggedOnly", $"{findings.Count} finding, max={maxSev}");
                    return;
                }

                string summary = string.Join(" | ", findings.OrderByDescending(f => f.Severity).Select(f => f.ToString()));
                string prompt = $"[Digest automatico periodico, nessun admin presente] Il controllo di sicurezza/salute " +
                                 $"del server (Windows, SQL, sito, gioco) ha rilevato: {summary}. Indaga con i tool " +
                                 "appropriati (corrobora con più sensori prima di agire) e segui i playbook — agisci " +
                                 "se la situazione lo giustifica, altrimenti spiega cosa hai trovato.";
                var result = Brain.Chat(prompt, null, AiTaskKind.Default,
                    msg => App.WriteLog("AI-DIGEST", msg), CancellationToken.None);

                string outcome = result.Cancelled ? "(annullato)" : result.Ok ? result.Answer : ("ERRORE: " + result.Error);
                App.WriteLog("AI-DIGEST", "Esito turno: " + outcome);
                OutcomeTracker.Record("security_digest", result.Ok ? "Executed" : "Failed", $"{summary}: {Truncate(outcome, 200)}");
                Notifier.NotifyDigest(_settings, maxSev, $"{summary}\n\nEsito:\n{Truncate(outcome, 3500)}");
            }
            catch (Exception ex) { App.WriteLog("AI-DIGEST", "errore tick: " + ex.Message); }
            finally { _busy = false; }
        }

        private List<Finding> CollectFindings()
        {
            var findings = new List<Finding>();
            int windowMin = (int)SweepInterval.TotalMinutes;

            // 1) Servizi di gioco fermi — SOLO visibilità: WatchLoop (ciclo separato, 3 min) è già
            // responsabile della diagnosi/riparazione, non duplicarla qui.
            var down = ServiceMonitor.Query()
                .Where(s => s.Installed && !s.Running &&
                            ServiceMonitor.GameServiceStartOrder.Contains(s.ServiceName, StringComparer.OrdinalIgnoreCase))
                .Select(s => s.ServiceName).ToList();
            if (down.Count > 0)
                findings.Add(new Finding(FindingSeverity.High, "servizi", $"servizi di gioco fermi: {string.Join(",", down)}"));

            // 2) Circuit breaker aperto — l'AI ha smesso di agire su T1+, l'admin deve saperlo anche
            // senza aprire la UI.
            if (PolicyGate.IsCircuitBreakerTripped)
                findings.Add(new Finding(FindingSeverity.High, "circuit-breaker", "circuit breaker AI APERTO (azioni T1+ sospese)"));

            // 3) Telemetria sicurezza gioco nella finestra (eventi significativi + login falliti)
            if (SecurityTelemetry.SignificantEvents(_settings, windowMin, out var evOut) == 0)
            {
                int n = CountRows(evOut);
                if (n > 0) findings.Add(new Finding(FindingSeverity.Medium, "sicurezza",
                    $"{n} evento/i di sicurezza significativi negli ultimi {windowMin} min"));
            }
            if (SecurityTelemetry.FailedLoginsByIp(_settings, windowMin, out var loginOut) == 0)
            {
                int n = CountRows(loginOut);
                if (n > 0) findings.Add(new Finding(FindingSeverity.Medium, "sicurezza",
                    $"login di gioco falliti da {n} IP negli ultimi {windowMin} min"));
            }

            // 4) Test suite — ultimo esito noto (Load() gestisce da solo "mai eseguita", HasData=false)
            var ts = TestSuiteReport.Load();
            if (ts.HasData && ts.FailTotal > 0)
                findings.Add(new Finding(FindingSeverity.Low, "test-suite", $"test suite: {ts.FailTotal} FAIL/ERRORE nell'ultimo run"));
            // WARN volutamente escluso: troppo comune/rumoroso, resta visibile in chat su richiesta (testsuite_status).

            // 5) Task pianificati in errore
            var failedTasks = TaskSchedulerService.List()
                .Where(t => TaskLogReader.LastRunState(t.Name) == TaskLogState.Error)
                .Select(t => t.Name).ToList();
            if (failedTasks.Count > 0)
                findings.Add(new Finding(FindingSeverity.Low, "task", $"task pianificati in errore: {string.Join(",", failedTasks)}"));

            // 6) Login OS falliti con IP reale (RDP/SSH/rete) — brute force a livello Windows, distinto
            // dal login DI GIOCO sopra. WindowsEventLog.FailedLogons filtra già il rumore interno noto
            // (MSSQLSERVER/Kerberos senza IP, verificato sul server reale).
            if (WindowsEventLog.FailedLogons(windowMin, out var osLogonOut) == 0 &&
                !osLogonOut.Contains("NESSUN_EVENTO_CON_IP_REALE"))
            {
                int maxPerIp = MaxTabCount(osLogonOut);
                if (maxPerIp >= _settings.AiDigestOsFailedLogonHigh)
                    findings.Add(new Finding(FindingSeverity.High, "os-logon", $"login OS falliti: fino a {maxPerIp} tentativi da un singolo IP negli ultimi {windowMin} min"));
                else if (maxPerIp >= _settings.AiDigestOsFailedLogonMedium)
                    findings.Add(new Finding(FindingSeverity.Medium, "os-logon", $"login OS falliti: fino a {maxPerIp} tentativi da un singolo IP negli ultimi {windowMin} min"));
            }

            // 7) Riavvii/crash inattesi (System 6008/41/1001, esclusi i falsi positivi noti tipo
            // "Hypervisor launch failed" già filtrati dentro WindowsEventLog.UnexpectedRestarts)
            if (WindowsEventLog.UnexpectedRestarts(windowMin, out var restartOut) == 0 &&
                !restartOut.Contains("NESSUN_RIAVVIO_INATTESO") && restartOut.Contains("NESSUN-1074-VICINO"))
            {
                findings.Add(new Finding(FindingSeverity.High, "os-reboot", $"riavvio/crash inatteso rilevato (nessuno shutdown pianificato vicino) negli ultimi {windowMin} min"));
            }

            // 8) Crash di servizio (7031/7034/7024) — copre anche servizi NON di gioco (SQL, web, SSH)
            if (WindowsEventLog.ServiceCrashes(windowMin, out var crashOut) == 0 &&
                !crashOut.Contains("NESSUN_CRASH_SERVIZIO"))
            {
                findings.Add(new Finding(FindingSeverity.Medium, "os-crash", $"crash di servizio rilevato negli ultimi {windowMin} min — vedi windows_event_log(focus:\"service_crashes\")"));
            }

            // 9) Spazio disco
            if (DiskSpaceSensor.Query(out var diskOut) == 0)
            {
                foreach (var line in (diskOut ?? "").Replace("\r", "").Split('\n'))
                {
                    var f = line.Split('\t');
                    if (f.Length >= 5 && f[0] == "DISK" && double.TryParse(f[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                    {
                        if (pct <= _settings.AiDigestDiskFreePctHigh)
                            findings.Add(new Finding(FindingSeverity.High, "disco", $"disco {f[1]} al {pct}% libero ({f[2]}GB/{f[3]}GB)"));
                        else if (pct <= _settings.AiDigestDiskFreePctMedium)
                            findings.Add(new Finding(FindingSeverity.Medium, "disco", $"disco {f[1]} al {pct}% libero ({f[2]}GB/{f[3]}GB)"));
                    }
                }
            }

            // 10) Salute SQL — blocking sostenuto
            if (SqlHealthSensor.BlockingChains(_settings, out var blockOut) == 0)
            {
                int maxWait = MaxColumnValue(blockOut, 2); // colonna WaitSeconds
                if (maxWait >= _settings.AiDigestSqlBlockingSecondsMedium)
                    findings.Add(new Finding(FindingSeverity.Medium, "sql", $"blocco SQL sostenuto da {maxWait}s"));
            }

            // 11) Salute sito web — riuso diretto di WebProbe/WebServerFactory (già usati da MainWindow.RefreshWeb)
            try
            {
                var webServer = WebServerFactory.Create(_settings);
                var webRes = WebProbe.Probe(webServer);
                if (!webRes.Reachable)
                    findings.Add(new Finding(FindingSeverity.High, "web", $"sito web IRRAGGIUNGIBILE — {webRes.Error}"));
                else if (!webRes.AppServer)
                    findings.Add(new Finding(FindingSeverity.Medium, "web", "sito web raggiungibile ma game server (status.php) DOWN"));
                if (webRes.CertExpiry.HasValue && (webRes.CertExpiry.Value.Date - DateTime.Now.Date).TotalDays <= 14)
                    findings.Add(new Finding(FindingSeverity.Medium, "web", $"certificato TLS in scadenza il {webRes.CertExpiry.Value:yyyy-MM-dd}"));
            }
            catch (Exception ex) { App.WriteLog("AI-DIGEST", "errore probe web: " + ex.Message); }

            // 12) SQL Server Audit (schema-change + login SQL falliti a livello server) — tailing per
            // marker persistito (SqlAuditSensor.TailNewFindings), MAI una finestra fissa: un evento non
            // viene mai segnalato due volte. No-op silenzioso se l'audit non è installato (Get-WinEvent
            // non trova il provider, nessuna eccezione, semplicemente nessun finding).
            try { SqlAuditSensor.TailNewFindings(_settings, findings); }
            catch (Exception ex) { App.WriteLog("AI-DIGEST", "errore sql audit: " + ex.Message); }

            return findings;
        }

        // sqlcmd -Q emette "(N rows affected)" in coda — modo affidabile per contare senza riparsare
        // manualmente header/separatore come fanno PlayerLookup/AiBanExecutor (qui basta il conteggio).
        private static int CountRows(string sqlcmdOutput)
        {
            var m = RowsAffected.Match(sqlcmdOutput ?? "");
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
        }

        // Righe tab-separated "IP\t<ip>\t<count>\t..." di WindowsEventLog.FailedLogons — prende il
        // conteggio massimo tra tutti gli IP (già ordinati per conteggio decrescente dallo script PS,
        // ma non ci si affida all'ordine: si scansiona tutto).
        private static int MaxTabCount(string output)
        {
            int max = 0;
            foreach (var line in (output ?? "").Replace("\r", "").Split('\n'))
            {
                var f = line.Split('\t');
                if (f.Length >= 3 && f[0] == "IP" && int.TryParse(f[2], out int n) && n > max) max = n;
            }
            return max;
        }

        // Valore massimo di una colonna numerica nell'output pipe-separated di sqlcmd (righe dati, salta
        // header/separatore/trailer "(N rows affected)").
        private static int MaxColumnValue(string sqlcmdOutput, int columnIndex)
        {
            int max = 0;
            var lines = (sqlcmdOutput ?? "").Replace("\r", "").Split('\n');
            for (int i = 2; i < lines.Length; i++) // riga 0=header, riga 1=separatore "----"
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.StartsWith("(")) continue;
                var cols = l.Split('|');
                if (columnIndex < cols.Length && int.TryParse(cols[columnIndex].Trim(), out int n) && n > max) max = n;
            }
            return max;
        }

        private static FindingSeverity ParseSeverity(string s, FindingSeverity fallback) =>
            Enum.TryParse<FindingSeverity>(s, true, out var sev) ? sev : fallback;

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";

        public void Dispose() => _timer?.Dispose();
    }
}
