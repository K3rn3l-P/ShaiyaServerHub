using System;
using System.IO;
using System.Linq;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Stato sintetico della rotazione chiavi per la card Dashboard e la vista Key Rotation:
    /// task pianificato (installato/abilitato/prossimo run), esito ultimo run e data ultima
    /// rotazione (mtime di GMSecurity.ini come proxy). Il calcolo di <see cref="Load"/> usa
    /// schtasks (lento) → chiamalo off-thread.
    /// </summary>
    public class KeyRotationReport
    {
        /// <summary>Nome breve del task nella cartella \ShaiyaServerHub\ del Task Scheduler.</summary>
        public const string TaskName = "KeyRotation";

        public bool TaskInstalled;
        public bool TaskEnabled;
        public string NextRun = "";
        public TaskLogState LastRun = TaskLogState.None;
        public bool HasIni;
        public DateTime LastRotation;   // mtime di GMSecurity.ini (proxy dell'ultima rotazione)

        public static KeyRotationReport Load(string gmSecurityIniPath)
        {
            var r = new KeyRotationReport();
            try
            {
                var t = TaskSchedulerService.List()
                    .FirstOrDefault(x => string.Equals(x.Name, TaskName, StringComparison.OrdinalIgnoreCase));
                if (t != null)
                {
                    r.TaskInstalled = true;
                    r.TaskEnabled = !t.IsDisabled;
                    r.NextRun = t.NextRun ?? "";
                }
                r.LastRun = TaskLogReader.LastRunState(TaskName);

                if (!string.IsNullOrWhiteSpace(gmSecurityIniPath) && File.Exists(gmSecurityIniPath))
                {
                    r.HasIni = true;
                    r.LastRotation = File.GetLastWriteTime(gmSecurityIniPath);
                }
            }
            catch { }
            return r;
        }
    }
}
