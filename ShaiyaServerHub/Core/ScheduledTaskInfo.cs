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
        public string Type { get; set; } = "DAILY";  // MINUTE | HOURLY | DAILY | WEEKLY | ONCE
        public int Interval { get; set; } = 1;        // per MINUTE/HOURLY (/MO)
        public string Time { get; set; } = "04:00";   // HH:mm (per DAILY/WEEKLY/ONCE)
        public string Days { get; set; } = "MON";     // per WEEKLY (/D)
    }
}
