namespace ShaiyaServerHub.Core
{
    /// <summary>Definizione completa di un task: azione (Exec) + pianificazione.</summary>
    public class TaskDefinition
    {
        public string Name { get; set; }
        public string Program { get; set; } = "";      // <Command>
        public string Arguments { get; set; } = "";     // <Arguments>
        public string WorkingDir { get; set; } = "";    // <WorkingDirectory> (Start in)
        public ScheduleSpec Schedule { get; set; } = new ScheduleSpec();
    }
}
