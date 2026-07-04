using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Wrapper su schtasks.exe. Tutti i task del Hub vivono nella cartella
    /// "\ShaiyaServerHub\" del Task Scheduler. L'azione del task richiama l'exe
    /// del Hub in modalità headless: ShaiyaServerHub.exe --run "&lt;scriptFullPath&gt;"
    /// (così auth/log restano centralizzati, nessuna password in chiaro nel task).
    /// </summary>
    public static class TaskSchedulerService
    {
        public const string Folder = "ShaiyaServerHub";

        public static string ExePath => Process.GetCurrentProcess().MainModule.FileName;
        public static string ExeDir => Path.GetDirectoryName(ExePath);

        // ── Create (azione custom: program + arguments + start-in) ──
        // wrapLogFile != null → dopo la creazione l'azione viene riscritta in
        // cmd.exe /c ""prog" args > "log" 2>&1" via PowerShell (quoting robusto).
        public static int Create(string taskName, ScheduleSpec spec,
                                 string program, string arguments, string workingDir, Action<string> log,
                                 string wrapLogFile = null, IEnumerable<ScheduleSpec> extraTriggers = null)
        {
            string shortName = Sanitize(taskName);
            string tn = $"\\{Folder}\\{shortName}";

            // /TR = SOLO il programma (token sicuro). Gli argomenti NON vanno qui:
            // le virgolette annidate di "--run \"script\"" (o del vecchio wrapper cmd)
            // collassano nel /TR "..." e schtasks fraintende (Invalid argument '--run').
            // L'azione vera (Execute+Arguments+WorkingDir) è impostata sotto via PowerShell.
            string tr = $"\\\"{TaskLogging.StripQuotes(program)}\\\"";

            string sc;
            switch ((spec.Type ?? "DAILY").ToUpperInvariant())
            {
                case "MINUTE": sc = $"/SC MINUTE /MO {Math.Max(1, spec.Interval)}"; break;
                case "HOURLY": sc = $"/SC HOURLY /MO {Math.Max(1, spec.Interval)}"; break;
                case "WEEKLY": sc = $"/SC WEEKLY /D {(string.IsNullOrWhiteSpace(spec.Days) ? "MON" : spec.Days)} /ST {spec.Time}"; break;
                case "ONCE": sc = $"/SC ONCE /ST {spec.Time}"; break;
                case "LOGON": sc = "/SC ONLOGON"; break;               // al login utente
                case "BOOT": sc = "/SC ONSTART"; break;                // all'avvio del sistema
                case "SHUTDOWN":                                        // allo spegnimento/riavvio (evento 1074)
                    sc = "/SC ONEVENT /EC System /MO \"*[System[Provider[@Name='User32'] and (EventID=1074)]]\"";
                    break;
                case "EVENT":                                           // trigger su evento custom (canale + XPath)
                    {
                        string ch = string.IsNullOrWhiteSpace(spec.EventChannel) ? "System" : spec.EventChannel;
                        string xp = (spec.EventXPath ?? "").Replace("\"", "\\\"");
                        sc = $"/SC ONEVENT /EC {ch} /MO \"{xp}\"";
                        // /DELAY mmmm:ss (valido con ONEVENT) → ritardo dopo l'evento.
                        if (spec.EventDelayMinutes > 0) sc += $" /DELAY {spec.EventDelayMinutes:0000}:00";
                    }
                    break;
                default: sc = $"/SC DAILY /ST {spec.Time}"; break;
            }

            string args = $"/Create /TN \"{tn}\" /TR \"{tr}\" {sc} /RU SYSTEM /RL HIGHEST /F";
            log?.Invoke($"[schtasks] creo task '{tn}'");
            int rc = ProcessRunner.Run("schtasks.exe", args, log);
            log?.Invoke(rc == 0 ? "✓ task creato" : $"✗ schtasks exit {rc}");
            if (rc != 0) return rc;

            if (!string.IsNullOrWhiteSpace(wrapLogFile))
                // Riscrive l'azione (Execute+Argument+WorkingDirectory) con redirect su log.
                SetWrappedAction(shortName, program, arguments, workingDir, wrapLogFile, log);
            else
                // Imposta SEMPRE l'azione vera via PowerShell (quoting robusto):
                // Execute=programma, Arguments=argomenti, WorkingDirectory=Start-in.
                SetAction(shortName, program, arguments, workingDir, log);

            // Trigger aggiuntivi (oltre al principale creato da schtasks) → appesi via PowerShell.
            var extras = extraTriggers?.Where(t => t != null).ToList();
            if (extras != null && extras.Count > 0)
                AddExtraTriggers(shortName, extras, log);
            return rc;
        }

        /// <summary>
        /// Appende trigger extra a un task esistente (Set-ScheduledTask). I trigger a tempo usano
        /// New-ScheduledTaskTrigger; quelli su evento un MSFT_TaskEventTrigger (CIM). Testato.
        /// </summary>
        public static void AddExtraTriggers(string shortName, IEnumerable<ScheduleSpec> specs, Action<string> log)
        {
            var list = specs?.Where(s => s != null).ToList();
            if (list == null || list.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("function New-EvtTrig($sub,$delay){ ");
            sb.Append("$c = Get-CimClass -Namespace Root/Microsoft/Windows/TaskScheduler -ClassName MSFT_TaskEventTrigger; ");
            sb.Append("$e = New-CimInstance -CimClass $c -ClientOnly; $e.Enabled = $true; $e.Subscription = $sub; ");
            sb.Append("if ($delay) { $e.Delay = $delay }; $e }; ");
            sb.Append($"$t = Get-ScheduledTask -TaskPath '\\{Folder}\\' -TaskName '{shortName.Replace("'", "''")}'; ");
            foreach (var s in list)
            {
                string expr = BuildTriggerPsExpr(s);
                if (expr != null) sb.Append($"$t.Triggers += {expr}; ");
            }
            sb.Append("$t | Set-ScheduledTask | Out-Null");

            int rc = ProcessRunner.RunPowerShellEncoded(sb.ToString(), log);
            log?.Invoke(rc == 0 ? $"✓ {list.Count} trigger aggiuntivi impostati" : $"⚠ trigger aggiuntivi non impostati (exit {rc})");
        }

        // Espressione PowerShell che valuta a un oggetto trigger, per un dato ScheduleSpec.
        private static string BuildTriggerPsExpr(ScheduleSpec s)
        {
            int mo = Math.Max(1, s.Interval);
            switch ((s.Type ?? "DAILY").ToUpperInvariant())
            {
                case "MINUTE": return $"(New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes {mo}))";
                case "HOURLY": return $"(New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Hours {mo}))";
                case "WEEKLY": return $"(New-ScheduledTaskTrigger -Weekly -DaysOfWeek {DaysToPs(s.Days)} -At '{s.Time}')";
                case "ONCE": return $"(New-ScheduledTaskTrigger -Once -At '{s.Time}')";
                case "LOGON": return "(New-ScheduledTaskTrigger -AtLogOn)";
                case "BOOT": return "(New-ScheduledTaskTrigger -AtStartup)";
                case "SHUTDOWN":
                    return "(New-EvtTrig '" + EventTriggerBuilder
                        .BuildQueryListXml("System", EventTriggerBuilder.ShutdownXPath).Replace("'", "''") + "' " + DelayArg(s) + ")";
                case "EVENT":
                    string ch = string.IsNullOrWhiteSpace(s.EventChannel) ? "System" : s.EventChannel;
                    string sub = EventTriggerBuilder.BuildQueryListXml(ch, s.EventXPath ?? "");
                    return "(New-EvtTrig '" + sub.Replace("'", "''") + "' " + DelayArg(s) + ")";
                default: return $"(New-ScheduledTaskTrigger -Daily -At '{s.Time}')";
            }
        }

        // Argomento delay per New-EvtTrig: 'PT{n}M' oppure $null.
        private static string DelayArg(ScheduleSpec s) =>
            s.EventDelayMinutes > 0 ? $"'PT{s.EventDelayMinutes}M'" : "$null";

        // "MON,WED" → "Monday,Wednesday" (per New-ScheduledTaskTrigger -DaysOfWeek).
        private static string DaysToPs(string csv)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "MON", "Monday" }, { "TUE", "Tuesday" }, { "WED", "Wednesday" }, { "THU", "Thursday" },
                { "FRI", "Friday" }, { "SAT", "Saturday" }, { "SUN", "Sunday" }
            };
            var days = (csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => map.TryGetValue(d.Trim(), out var v) ? v : null)
                .Where(v => v != null).ToList();
            return days.Count > 0 ? string.Join(",", days) : "Monday";
        }

        // Riscrive l'azione come cmd wrapper con redirect su file di log (quoting via PowerShell).
        private static void SetWrappedAction(string shortName, string program, string arguments,
                                             string workingDir, string logFile, Action<string> log)
        {
            string argument = TaskLogging.BuildCmdWrapperArgument(program, arguments, logFile);
            string wd = string.IsNullOrWhiteSpace(workingDir) ? "" :
                        $" -WorkingDirectory '{workingDir.Replace("'", "''")}'";
            string ps =
                $"$a = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '{argument.Replace("'", "''")}'{wd}; " +
                $"$t = Get-ScheduledTask -TaskPath '\\{Folder}\\' -TaskName '{shortName.Replace("'", "''")}'; " +
                "$t.Actions = @($a); $t | Set-ScheduledTask | Out-Null";
            // -EncodedCommand: le virgolette annidate del wrapper cmd NON sopravvivono a -Command.
            int rc = ProcessRunner.RunPowerShellEncoded(ps, log);
            log?.Invoke(rc == 0 ? "✓ log su file abilitato (log\\)" : "⚠ log su file non impostato (exit " + rc + ")");
        }

        // Imposta l'azione reale del task (Execute + Arguments + WorkingDirectory) via PowerShell.
        // -EncodedCommand: gli argomenti possono contenere virgolette (es. --run "path", --log "path")
        // che NON sopravviverebbero a un /TR di schtasks né a -Command.
        private static void SetAction(string shortName, string program, string arguments,
                                      string workingDir, Action<string> log)
        {
            string exec = TaskLogging.StripQuotes(program).Replace("'", "''");
            string argPart = string.IsNullOrWhiteSpace(arguments) ? "" :
                             $" -Argument '{arguments.Trim().Replace("'", "''")}'";
            string wd = string.IsNullOrWhiteSpace(workingDir) ? "" :
                        $" -WorkingDirectory '{workingDir.Replace("'", "''")}'";
            string ps =
                $"$a = New-ScheduledTaskAction -Execute '{exec}'{argPart}{wd}; " +
                $"$t = Get-ScheduledTask -TaskPath '\\{Folder}\\' -TaskName '{shortName.Replace("'", "''")}'; " +
                "$t.Actions = @($a); $t | Set-ScheduledTask | Out-Null";
            int rc = ProcessRunner.RunPowerShellEncoded(ps, log);
            log?.Invoke(rc == 0 ? "✓ azione impostata" : "⚠ azione non impostata (exit " + rc + ")");
        }

        /// <summary>XML completo del task (per il modale Edit).</summary>
        public static string GetXml(string fullName)
        {
            string xml;
            ProcessRunner.RunCapture("schtasks.exe", $"/Query /TN \"{fullName}\" /XML", out xml);
            return xml;
        }

        // ── Enable / Disable / Delete ────────────────────────
        public static int SetEnabled(string fullName, bool enabled, Action<string> log)
        {
            string flag = enabled ? "/ENABLE" : "/DISABLE";
            int rc = ProcessRunner.Run("schtasks.exe", $"/Change /TN \"{fullName}\" {flag}", log);
            log?.Invoke(rc == 0 ? (enabled ? "✓ abilitato" : "✓ disabilitato") : $"✗ schtasks exit {rc}");
            return rc;
        }

        public static int Delete(string fullName, Action<string> log)
        {
            int rc = ProcessRunner.Run("schtasks.exe", $"/Delete /TN \"{fullName}\" /F", log);
            log?.Invoke(rc == 0 ? "✓ rimosso" : $"✗ schtasks exit {rc}");
            return rc;
        }

        public static int RunNow(string fullName, Action<string> log)
        {
            int rc = ProcessRunner.Run("schtasks.exe", $"/Run /TN \"{fullName}\"", log);
            return rc;
        }

        // ── List (CSV parse) ─────────────────────────────────
        public static List<ScheduledTaskInfo> List()
        {
            var result = new List<ScheduledTaskInfo>();
            string csv;
            // Enumera TUTTI i task e filtra per cartella \ShaiyaServerHub\.
            // (Non si può interrogare una cartella con /TN "\ShaiyaServerHub\" → ERROR syntax.)
            // NB: non usciamo su rc != 0 — schtasks può dare rc≠0 per un task di sistema
            //     rotto ma l'output resta parsabile per i nostri.
            ProcessRunner.RunCapture("schtasks.exe", "/Query /FO CSV /V", out csv);
            if (string.IsNullOrWhiteSpace(csv)) return result;

            string[] lines = csv.Replace("\r", "").Split('\n');
            if (lines.Length < 2) return result;

            string[] header = SplitCsv(lines[0]);
            int idxName = IndexOf(header, "TaskName");
            int idxStatus = IndexOf(header, "Status");
            int idxNext = IndexOf(header, "Next Run Time");
            int idxSchedType = IndexOf(header, "Schedule Type");
            int idxStart = IndexOf(header, "Start Time");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] c = SplitCsv(lines[i]);
                if (idxName < 0 || idxName >= c.Length) continue;
                string full = c[idxName];
                if (full.Equals("TaskName", StringComparison.OrdinalIgnoreCase)) continue;
                if (!full.StartsWith($"\\{Folder}\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(full)) continue; // /V ripete righe per trigger multipli

                result.Add(new ScheduledTaskInfo
                {
                    FullName = full,
                    Name = full.Substring(full.LastIndexOf('\\') + 1),
                    Status = Get(c, idxStatus),
                    NextRun = Get(c, idxNext),
                    Schedule = (Get(c, idxSchedType) + " " + Get(c, idxStart)).Trim()
                });
            }
            return result.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ── Helpers ──────────────────────────────────────────
        private static string Get(string[] a, int i) => (i >= 0 && i < a.Length) ? a[i] : "";
        private static int IndexOf(string[] a, string name)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static string[] SplitCsv(string line)
        {
            var fields = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQ = !inQ;
                }
                else if (ch == ',' && !inQ) { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            fields.Add(cur.ToString());
            return fields.ToArray();
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace('\\', '_');
        }
    }
}
