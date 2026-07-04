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
    }

    /// <summary>Cadenza per creare un nuovo task.</summary>
    public class ScheduleSpec
    {
        public string Type { get; set; } = "DAILY";  // MINUTE | HOURLY | DAILY | WEEKLY | ONCE | LOGON | BOOT | SHUTDOWN | EVENT
        public int Interval { get; set; } = 1;        // per MINUTE/HOURLY (/MO)
        public string Time { get; set; } = "04:00";   // HH:mm (per DAILY/WEEKLY/ONCE)
        public string Days { get; set; } = "MON";     // per WEEKLY (/D)

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
