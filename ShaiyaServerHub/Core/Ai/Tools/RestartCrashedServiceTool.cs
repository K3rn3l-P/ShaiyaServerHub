using System;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Primo tool T1 (auto-heal) del Hub — vedi PLAN §Modello di autonomia, §PROSSIMO BLOCCO P2
    /// Blocco 1. Riavvia un servizio di gioco SOLO se: (1) è nella whitelist di
    /// <see cref="Invariants.CheckServiceRestart"/> (verificato da <see cref="PolicyGate"/> PRIMA che
    /// <see cref="Execute"/> venga anche solo chiamato — questa classe non deve MAI essere l'unica
    /// linea di difesa); (2) è REALMENTE fermo in questo momento (ri-verificato qui con
    /// <see cref="ServiceMonitor.Query(System.ValueTuple{string,string}[])"/> appena prima di agire —
    /// mai fidarsi che il modello abbia letto uno stato corretto/recente, riavviare un servizio SANO
    /// per errore sarebbe uno spreco/rischio inutile). Riusa <see cref="ServiceMonitor.Start"/>
    /// esistente, nessuna nuova plumbing di controllo servizi.
    /// </summary>
    public class RestartCrashedServiceTool : IAiTool
    {
        public string Name => "restart_crashed_service";
        public string Description => "Riavvia un servizio di gioco Windows SOLO se risulta realmente fermo " +
            "(ri-verificato prima di agire) — whitelist: ps_game, ps_login, ps_session, ps_gameLog, ps_userLog, " +
            "ps_dbAgent. Mai infrastruttura (MSSQLSERVER/sshd/PSM_*). Se il servizio è già in esecuzione: no-op. " +
            "Azione T1 (auto-heal), reversibile, soggetta a PolicyGate.";
        public AiToolTier Tier => AiToolTier.T1_AutoHeal;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"service\":{\"type\":\"string\",\"description\":\"Nome del servizio Windows (es. ps_game)\"}}," +
            "\"required\":[\"service\"]}";

        public RestartCrashedServiceTool(AiToolContext ctx) { }

        public string Execute(string argumentsJson)
        {
            try
            {
                string service = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    if (doc.RootElement.TryGetProperty("service", out var s) && s.ValueKind == JsonValueKind.String)
                        service = s.GetString();
                }
                if (string.IsNullOrWhiteSpace(service)) return "ERRORE: parametro 'service' mancante.";

                // Ri-verifica lo stato REALE, non fidarsi di quanto il modello crede di sapere — PolicyGate
                // ha già validato che il nome sia in whitelist, qui verifichiamo che sia davvero il momento di agire.
                var status = ServiceMonitor.Query(new[] { (service, service) }).FirstOrDefault();
                if (status == null || !status.Installed)
                    return $"'{service}' non risulta installato su questo server — nessuna azione.";
                if (status.Running)
                    return $"'{service}' è già in esecuzione — nessuna azione necessaria (no-op).";

                var sb = new StringBuilder();
                ServiceMonitor.Start(service, msg => sb.AppendLine(msg));

                // Verifica finale post-azione (non fidarsi del solo esito del comando Start).
                var after = ServiceMonitor.Query(new[] { (service, service) }).FirstOrDefault();
                sb.AppendLine(after != null && after.Running
                    ? $"Verifica: '{service}' ora RUNNING."
                    : $"Verifica: '{service}' NON risulta RUNNING dopo il tentativo — richiede intervento manuale.");
                return sb.ToString();
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
