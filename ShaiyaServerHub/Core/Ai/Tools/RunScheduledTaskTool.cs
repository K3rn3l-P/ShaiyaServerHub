using System;
using System.Linq;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool "task-autofix" di P3: colma "diagnosi→azione" per i task pianificati (finora l'AI vedeva
    /// solo OK/ERRORE via <see cref="TasksStatusTool"/>, mai poteva rilanciare). Sempre T3
    /// (Reversible=false, come <see cref="BanPlayerTool"/>) perché il CONTENUTO di un task è arbitrario
    /// — può essere qualsiasi script SQL/PS1 che l'admin ha configurato (key rotation, manutenzione,
    /// backup, script custom), l'AI non può verificarne staticamente la sicurezza. Approvazione umana
    /// SEMPRE richiesta, nessuna eccezione per urgency/confidence — stesso principio di ban_player.
    /// </summary>
    public class RunScheduledTaskTool : IAiTool
    {
        public string Name => "run_scheduled_task";
        public string Description => "Esegue SUBITO un task pianificato del Hub (come premere '▶ Esegui ora' nel " +
            "pannello Task pianificati) — usalo DOPO aver diagnosticato un errore con " +
            "scheduled_tasks_status({\"task\":\"<nome>\"}) (leggi SEMPRE il log completo prima, non indovinare la " +
            "causa dal solo esito OK/ERRORE). Il contenuto del task è arbitrario (script SQL/PS1 configurato " +
            "dall'admin) — richiede SEMPRE approvazione umana via Telegram, mai auto-eseguito, indipendentemente da " +
            "urgency/confidence. Azione T3, irreversibile per costruzione (effetti del task non verificabili a priori).";
        public AiToolTier Tier => AiToolTier.T3_Risky;
        public bool Reversible => false;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"task\":{\"type\":\"string\",\"description\":\"Nome esatto del task (vedi scheduled_tasks_status)\"}," +
            "\"reason\":{\"type\":\"string\",\"description\":\"Perché lo stai rilanciando (onesto, finisce nel log/Telegram)\"}," +
            "\"urgency\":{\"type\":\"string\",\"enum\":[\"nessuno\",\"lento\",\"veloce\",\"critico\"]}," +
            "\"confidence\":{\"type\":\"number\"}}," +
            "\"required\":[\"task\",\"reason\"]}";

        public string Execute(string argumentsJson)
        {
            try
            {
                string task = null, reason = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("task", out var t) && t.ValueKind == JsonValueKind.String) task = t.GetString();
                    if (root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String) reason = r.GetString();
                }

                // Ri-verifica anche qui (difesa in profondità, come per gli altri tool T2/T3).
                if (!Invariants.CheckRunScheduledTask(task, reason, out var denyReason))
                    return "RIFIUTATO: " + denyReason;

                var match = TaskSchedulerService.List()
                    .FirstOrDefault(x => string.Equals(x.Name, task, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    return $"RIFIUTATO: task '{task}' non trovato tra quelli pianificati (\\ShaiyaServerHub\\).";

                string log = "";
                int rc = TaskSchedulerService.RunNow(match.FullName, msg => log += msg + "\n");
                return rc == 0
                    ? $"Task '{match.Name}' avviato ({match.FullName}) — motivo: \"{reason}\". Verifica l'esito tra " +
                      $"qualche istante con scheduled_tasks_status({{\"task\":\"{match.Name}\"}})."
                    : $"ERRORE avvio task '{match.Name}' (rc={rc}): {log}";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
