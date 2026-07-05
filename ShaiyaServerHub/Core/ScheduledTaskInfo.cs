namespace ShaiyaServerHub.Core
{
    public class ScheduledTaskInfo
    {
        public string Name { get; set; }        // nome breve (senza il prefisso cartella)
        public string FullName { get; set; }     // \ShaiyaServerHub\<Name>
        public string Status { get; set; }       // Ready / Disabled / Running
        public string NextRun { get; set; }
        public string Schedule { get; set; }

        public bool IsDisabled =>
            (Status ?? "").IndexOf("Disab", System.StringComparison.OrdinalIgnoreCase) >= 0;

        // ── Log per-task (bottone "Log" in Pianificati) ──
        /// <summary>Esito dell'ultimo run dal log (impostato da ReloadTasks).</summary>
        public TaskLogState LogState { get; set; } = TaskLogState.None;

        /// <summary>True se esiste un log per questo task (bottone abilitato).</summary>
        public bool HasLog => LogState != TaskLogState.None;

        private static readonly System.Windows.Media.Brush Green =
            Freeze(System.Windows.Media.Color.FromRgb(0x1E, 0x7A, 0x46));  // ultimo run OK
        private static readonly System.Windows.Media.Brush Red =
            Freeze(System.Windows.Media.Color.FromRgb(0x8A, 0x2A, 0x2A));  // ultimo run in errore
        private static readonly System.Windows.Media.Brush Gray =
            Freeze(System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x33));  // nessun log

        /// <summary>Sfondo del bottone Log: verde=OK, rosso=errore, grigio=nessun log.</summary>
        public System.Windows.Media.Brush LogBrush =>
            LogState == TaskLogState.Ok ? Green : (LogState == TaskLogState.Error ? Red : Gray);

        /// <summary>Tooltip del bottone Log.</summary>
        public string LogTip =>
            LogState == TaskLogState.Ok ? L.TaskLogTipOk :
            LogState == TaskLogState.Error ? L.TaskLogTipErr :
            L.TaskLogTipNone;

        private static System.Windows.Media.Brush Freeze(System.Windows.Media.Color c)
        {
            var b = new System.Windows.Media.SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    /// <summary>Cadenza per creare un nuovo task.</summary>
    public class ScheduleSpec
    {
        public string Type { get; set; } = "DAILY";  // MINUTE | HOURLY | DAILY | WEEKLY | MONTHLY | ONCE | LOGON | BOOT | SHUTDOWN | EVENT
        public int Interval { get; set; } = 1;        // per MINUTE/HOURLY (/MO)
        public string Time { get; set; } = "04:00";   // HH:mm (per DAILY/WEEKLY/MONTHLY/ONCE)
        public string Days { get; set; } = "MON";     // per WEEKLY (/D)

        // ── MONTHLY (ScheduleByMonth: giorni-del-mese + mesi selezionati) ──
        public string MonthDays { get; set; } = "1";  // CSV giorni del mese 1..31 (/D), es. "21" o "1,15"
        public string Months { get; set; } = "";      // CSV mesi (/M): "JAN,APR,JUL,OCT" — vuoto = tutti i mesi (*)

        // ── EVENT (trigger su evento del log di Windows) ──
        public string EventChannel { get; set; } = "System"; // /EC (es. System, Application)
        public string EventXPath { get; set; } = "";         // /MO (query XPath del filtro evento)
        public int EventDelayMinutes { get; set; } = 0;       // ritardo dopo l'evento (0 = nessuno)

        /// <summary>Etichetta breve per la UI (binding lista trigger).</summary>
        public string Label => Describe();

        /// <summary>Etichetta breve per la UI (lista trigger).</summary>
        public string Describe()
        {
            switch ((Type ?? "DAILY").ToUpperInvariant())
            {
                case "MINUTE": return $"MINUTE · ogni {System.Math.Max(1, Interval)} min";
                case "HOURLY": return $"HOURLY · ogni {System.Math.Max(1, Interval)} h";
                case "WEEKLY": return $"WEEKLY · {Days} · {Time}";
                case "MONTHLY":
                    string mm = string.IsNullOrWhiteSpace(Months) ? "ogni mese" : Months;
                    string dd = string.IsNullOrWhiteSpace(MonthDays) ? "1" : MonthDays;
                    return $"MONTHLY · g.{dd} · {mm} · {Time}";
                case "ONCE": return $"ONCE · {Time}";
                case "LOGON": return "LOGON";
                case "BOOT": return "BOOT";
                case "SHUTDOWN": return "SHUTDOWN (1074)";
                case "EVENT":
                    var m = System.Text.RegularExpressions.Regex.Match(EventXPath ?? "", @"param1'\] and \(Data='([^']+)'");
                    string ev = m.Success ? $"EVENT · {m.Groups[1].Value}" : "EVENT";
                    return EventDelayMinutes > 0 ? $"{ev} (+{EventDelayMinutes}min)" : ev;
                default: return $"DAILY · {Time}";
            }
        }
    }
}
