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

        private static string ExePath => Process.GetCurrentProcess().MainModule.FileName;

        // ── Create ───────────────────────────────────────────
        public static int Create(string taskName, string scriptFullPath, ScheduleSpec spec, Action<string> log)
        {
            string tn = $"\\{Folder}\\{Sanitize(taskName)}";
            string tr = $"\\\"{ExePath}\\\" --run \\\"{scriptFullPath}\\\"";

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
                default: sc = $"/SC DAILY /ST {spec.Time}"; break;
            }

            string args = $"/Create /TN \"{tn}\" /TR \"{tr}\" {sc} /RU SYSTEM /RL HIGHEST /F";
            log?.Invoke($"[schtasks] creo task '{tn}' → {scriptFullPath}");
            int rc = ProcessRunner.Run("schtasks.exe", args, log);
            log?.Invoke(rc == 0 ? "✓ task creato" : $"✗ schtasks exit {rc}");
            return rc;
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
