using System;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: scansione LIVE dei punti di autostart Windows (Run keys, servizi, driver, task,
    /// AppInit_DLLs, WMI subscriptions, ecc. — 100+ location) via Sysinternals autorunsc, confrontata
    /// contro il baseline salvato — colma il gap "config-drift"/persistenza mai coperto dai 21 controlli
    /// Test Suite esistenti (quelli sono infra-security, non ASEP). Riusa lo STESSO script/baseline
    /// della Test Suite (<see cref="ExternalToolsManager.RunAutorunsBaselineCheck"/>,
    /// 22_autoruns_baseline.ps1) — nessun parsing duplicato. Diverso da testsuite_status: quello legge
    /// l'ULTIMO run salvato nel log, questo esegue una scansione fresca adesso (richiesta esplicita
    /// tipo "controlla se c'è qualcosa di nuovo in autostart").
    /// </summary>
    public class AutorunsAuditTool : IAiTool
    {
        public string Name => "autoruns_audit";
        public string Description => "Scansione LIVE (Sysinternals autorunsc) di tutti i punti di autostart Windows " +
            "confrontata contro il baseline salvato. PASS = nessuna variazione, WARN = voci NUOVE rispetto al " +
            "baseline (persistenza sospetta/config-drift, es. una backdoor in AppInit_DLLs o un servizio nuovo non " +
            "atteso), INFO = tool non ancora scaricato dall'admin oppure baseline creato ora al primo run (nessun " +
            "confronto ancora possibile). Nessun parametro. Read-only, non muta nulla.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{}}";

        public string Execute(string argumentsJson)
        {
            try
            {
                if (!ExternalToolsManager.IsAutorunscPresent)
                    return "autorunsc.exe non è ancora scaricato — l'admin deve premere \"Scarica/Aggiorna Autoruns\" " +
                           "nella vista Test Suite prima che questo controllo sia disponibile.";

                ExternalToolsManager.RunAutorunsBaselineCheck(out var status, out var detail);
                return $"[{status}] {detail}";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
