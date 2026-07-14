using System;
using System.Linq;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>Tool T0: elenco task pianificati del Hub + esito ultimo run. Wrappa TaskSchedulerService/TaskLogReader esistenti.</summary>
    public class TasksStatusTool : IAiTool
    {
        public string Name => "scheduled_tasks_status";
        public string Description => "Elenco dei task pianificati del Hub (cartella \\ShaiyaServerHub\\) con " +
            "stato, prossima esecuzione ed esito dell'ultimo run (OK/ERRORE/mai eseguito). Read-only. Passa 'task' " +
            "(nome esatto) per il LOG COMPLETO di un task specifico — usalo PRIMA di proporre run_scheduled_task, per " +
            "capire la vera causa dell'errore invece di indovinare dal solo esito OK/ERRORE.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{\"task\":{\"type\":\"string\",\"description\":\"Nome esatto di un task per il log completo — omesso per l'elenco riassuntivo\"}}}";

        public TasksStatusTool(AiToolContext ctx) { }

        public string Execute(string argumentsJson)
        {
            try
            {
                string taskName = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                    if (doc.RootElement.TryGetProperty("task", out var t) && t.ValueKind == JsonValueKind.String) taskName = t.GetString();

                var tasks = TaskSchedulerService.List();
                if (tasks.Count == 0) return "Nessun task pianificato trovato.";

                if (!string.IsNullOrWhiteSpace(taskName))
                {
                    var match = tasks.FirstOrDefault(t => string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase));
                    if (match == null) return $"Task '{taskName}' non trovato tra quelli pianificati (\\ShaiyaServerHub\\).";

                    var state = TaskLogReader.LastRunState(match.Name);
                    string header = $"{match.Name}: stato={match.Status} · prossimo={match.NextRun} · schedule={match.Schedule} · ultimo esito={state}";
                    string log = TaskLogReader.Read(match.Name, out var path);
                    return log != null
                        ? $"{header}\n\nLog ({path}):\n{log}"
                        : $"{header}\n\nNessun log dedicato disponibile (opzione \"Genera log\" non abilitata alla creazione di questo task).";
                }

                return string.Join("\n", tasks.Select(t =>
                {
                    var lastRun = TaskLogReader.LastRunState(t.Name);
                    return $"{t.Name}: {t.Status} · prossimo={t.NextRun} · schedule={t.Schedule} · ultimo esito={lastRun}";
                }));
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
