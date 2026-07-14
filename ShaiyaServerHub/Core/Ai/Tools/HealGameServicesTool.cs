using System;
using System.Linq;
using System.Text;
using System.Threading;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T1 — auto-heal di TUTTI i servizi di gioco fermi, nell'ORDINE CORRETTO di dipendenza
    /// (<see cref="ServiceMonitor.GameServiceStartOrder"/>). Aggiunto dopo un test reale in cui il
    /// modello ha riavviato i servizi con `restart_crashed_service` uno alla volta nell'ordine in cui
    /// GLI SEMBRAVA logico (es. ps_game per primo) invece dell'ordine di dipendenza reale (ps_game si
    /// connette a dbAgent/gameLog/session appena parte — vedi log "connect dbagent"/"connect gamelog") —
    /// i servizi ripartivano un istante poi ricadevano giù. L'ordine giusto non è qualcosa che un LLM
    /// generico può indovinare in modo affidabile: va imposto nel codice, non lasciato alla scelta del
    /// modello per ogni singola chiamata (stesso principio "guardrail nel codice" di tutto il resto).
    /// Nessun parametro dal modello: opera SEMPRE sulla whitelist fissa (nessuna superficie di
    /// iniezione) — vedi <see cref="Invariants.CheckHealAllGameServices"/>.
    /// </summary>
    public class HealGameServicesTool : IAiTool
    {
        public string Name => "heal_game_services";
        public string Description => "Riavvia TUTTI i servizi di gioco attualmente fermi, nell'ordine di dipendenza " +
            "corretto (ps_session → ps_userLog → ps_login → ps_gameLog → ps_dbAgent → ps_game), con verifica di " +
            "stabilità (attesa + ricontrollo) prima di passare al successivo — si ferma se uno non si stabilizza " +
            "invece di continuare su una base rotta. USA QUESTO invece di restart_crashed_service ripetuto quando " +
            "PIÙ servizi di gioco sono fermi insieme: l'ordine conta, Shaiya ha dipendenze reali all'avvio tra " +
            "servizi. Nessun parametro. Azione T1 (auto-heal), reversibile, soggetta a PolicyGate.";
        public AiToolTier Tier => AiToolTier.T1_AutoHeal;
        public bool Reversible => true;
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{}}";

        public HealGameServicesTool(AiToolContext ctx) { }

        public string Execute(string argumentsJson)
        {
            try
            {
                var sb = new StringBuilder();
                bool anyDown = false;

                foreach (var svc in ServiceMonitor.GameServiceStartOrder)
                {
                    var status = ServiceMonitor.Query(new[] { (svc, svc) }).FirstOrDefault();
                    if (status == null || !status.Installed) { sb.AppendLine($"{svc}: non installato — salto."); continue; }
                    if (status.Running) { sb.AppendLine($"{svc}: già RUNNING — salto."); continue; }

                    anyDown = true;
                    sb.AppendLine($"{svc}: STOPPED — avvio...");
                    ServiceMonitor.Start(svc, msg => sb.AppendLine("  " + msg));

                    // Stabilizzazione: un "Running" immediato dello Service Control Manager non garantisce
                    // che il processo non ricada pochi secondi dopo per una dipendenza non ancora pronta —
                    // osservato nel test reale. Attendi e riverifica prima di procedere al prossimo servizio.
                    Thread.Sleep(4000);
                    var after = ServiceMonitor.Query(new[] { (svc, svc) }).FirstOrDefault();
                    if (after == null || !after.Running)
                    {
                        sb.AppendLine($"  ✗ {svc} NON si è stabilizzato (ricaduto o mai partito) — FERMO la " +
                                       "sequenza qui: i servizi successivi dipendono da questo e partirebbero rotti.");
                        return sb.ToString();
                    }
                    sb.AppendLine($"  ✓ {svc} stabile (RUNNING dopo 4s).");
                }

                if (!anyDown) sb.Insert(0, "Nessun servizio di gioco era fermo — nessuna azione necessaria.\n");
                return sb.ToString();
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
