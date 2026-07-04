using System.Collections.Generic;

namespace ShaiyaServerHub.Core
{
    /// <summary>Definizione completa di un task: azione (Exec) + uno o più trigger.</summary>
    public class TaskDefinition
    {
        public string Name { get; set; }
        public string Program { get; set; } = "";      // <Command>
        public string Arguments { get; set; } = "";     // <Arguments>
        public string WorkingDir { get; set; } = "";    // <WorkingDirectory> (Start in)

        /// <summary>True se il task aveva il logging attivo (auto-rilevato in Edit: wrapper cmd / -o / --log).</summary>
        public bool GenLog { get; set; }

        /// <summary>Tutti i trigger del task (0..n). Il primo è il "principale".</summary>
        public List<ScheduleSpec> Triggers { get; set; } = new List<ScheduleSpec>();

        /// <summary>Primo trigger (compat): quello mostrato come principale nel form.</summary>
        public ScheduleSpec Schedule
        {
            get => Triggers.Count > 0 ? Triggers[0] : new ScheduleSpec();
            set { if (Triggers.Count > 0) Triggers[0] = value; else Triggers.Add(value); }
        }
    }
}
