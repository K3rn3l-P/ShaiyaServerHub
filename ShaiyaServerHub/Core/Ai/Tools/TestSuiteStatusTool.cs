using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: esito della Test Suite di sicurezza. Senza parametri torna solo il conteggio aggregato
    /// (come prima — economico). Con `detail:true` elenca OGNI controllo non-PASS CON LA RIGA DI
    /// VERDETTO REALE (RESULT:/Detail: dal log, via <see cref="ExtractVerdict"/>) — non solo nome+stato:
    /// osservato che con solo nome+stato un modello free/debole, invece di richiamare il tool una volta
    /// per script (7 volte per un run tipico), INDOVINAVA il contenuto dal nome dello script e lo
    /// presentava come dato reale (es. per "07_clr_assemblies.sql" ha inventato "sdev.dll ha permessi
    /// UNSAFE" quando il log reale parlava di un assembly PSMagent EXTERNAL_ACCESS — tutta un'altra
    /// cosa). Guardrail nel codice, non nel prompt: il verdetto vero arriva già nella prima chiamata,
    /// niente da indovinare. Con `script:"<nome>"` resta disponibile il blocco di log COMPLETO per un
    /// approfondimento mirato (stessa fonte dei badge PASS/WARN/FAIL della vista Test Suite —
    /// <see cref="AreaLogParser"/>, ultimo run vince).
    /// </summary>
    public class TestSuiteStatusTool : IAiTool
    {
        public string Name => "testsuite_status";
        public string Description => "Esito della Test Suite di sicurezza (controlli SQL Server, sistema operativo, " +
            "rete e sito web). Senza parametri: solo il conteggio PASS/WARN/FAIL/ERRORE. Con {\"detail\":true}: " +
            "elenca OGNI controllo non-PASS con il suo verdetto reale (motivo, non solo nome+esito) — NON serve " +
            "richiamare il tool per ogni singolo controllo, il motivo è già qui. Con {\"script\":\"<nome>\"}: il log " +
            "completo di quello specifico controllo, solo se serve approfondire oltre il verdetto. Read-only.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"detail\":{\"type\":\"boolean\",\"description\":\"Se true, elenca ogni controllo non-PASS con il suo esito\"}," +
            "\"script\":{\"type\":\"string\",\"description\":\"Nome di un controllo specifico: torna il suo log completo\"}}}";

        public TestSuiteStatusTool(AiToolContext ctx) { }

        public string Execute(string argumentsJson)
        {
            try
            {
                bool detail = false;
                string script = null;
                try
                {
                    using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                    {
                        if (doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.True) detail = true;
                        if (doc.RootElement.TryGetProperty("script", out var s) && s.ValueKind == JsonValueKind.String) script = s.GetString();
                    }
                }
                catch { /* argomenti assenti/malformati: comportamento di default (solo conteggio) */ }

                string logPath = Path.Combine(ScriptRepository.TestSuite.Root, "testsuite.log");
                var map = AreaLogParser.ParsePerScript(logPath);
                if (map.Count == 0) return "Nessun run della Test Suite registrato ancora.";

                if (!string.IsNullOrWhiteSpace(script))
                {
                    if (!map.TryGetValue(script, out var one))
                        return $"Controllo '{script}' non trovato nell'ultimo run. Nomi disponibili: {string.Join(", ", map.Keys)}";
                    return $"{script} → {one.Status}\n{one.Log}";
                }

                var r = TestSuiteReport.Load();
                string summary = $"Ultimo run: {r.When:yyyy-MM-dd HH:mm:ss} — PASS={r.Pass} WARN={r.Warn} " +
                                  $"FAIL={r.Fail} ERRORE={r.Error} INFO={r.Info} (totale {r.Total})";
                if (!detail)
                    return summary + " · per sapere QUALI controlli hanno WARN/FAIL richiama con {\"detail\":true}";

                var nonPass = map.Where(kv => !string.Equals(kv.Value.Status, "PASS", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => kv.Value.Status == "FAIL" || kv.Value.Status == "ERRORE" ? 0 : kv.Value.Status == "WARN" ? 1 : 2)
                    .ToList();
                if (nonPass.Count == 0) return summary + " · tutti i controlli sono PASS.";

                var sb = new StringBuilder(summary);
                sb.AppendLine();
                sb.AppendLine("Controlli non-PASS (verdetto reale dal log — richiama con {\"script\":\"<nome>\"} solo se serve il log completo):");
                foreach (var kv in nonPass) sb.AppendLine($"- {kv.Key} → {kv.Value.Status}: {ExtractVerdict(kv.Value.Log)}");
                return sb.ToString();
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }

        /// <summary>
        /// Estrae la riga di verdetto leggibile da un blocco di log: `RESULT: ...` per gli script SQL,
        /// `Detail : ...` per quelli PowerShell (formati emessi da ScriptRunner/testsuite — vedi i due
        /// script reali: `RESULT: WARN - 1 assembly EXTERNAL_ACCESS/UNSAFE` oppure
        /// `Detail : ForceEncryption=0 ; Certificate=nessuno ...`). Fallback: ultima riga non vuota.
        /// </summary>
        private static string ExtractVerdict(string log)
        {
            if (string.IsNullOrWhiteSpace(log)) return "(nessun dettaglio nel log)";
            string result = null, detail = null;
            var lines = log.Replace("\r", "").Split('\n');
            foreach (var raw in lines)
            {
                string ln = raw.Trim();
                int ri = ln.IndexOf("RESULT:", StringComparison.OrdinalIgnoreCase);
                if (ri >= 0) result = ln.Substring(ri);
                else if (ln.StartsWith("Detail", StringComparison.OrdinalIgnoreCase))
                {
                    int c = ln.IndexOf(':');
                    if (c >= 0) detail = "Detail: " + ln.Substring(c + 1).Trim();
                }
            }
            if (result != null) return result;
            if (detail != null) return detail;
            for (int i = lines.Length - 1; i >= 0; i--)
                if (!string.IsNullOrWhiteSpace(lines[i])) return lines[i].Trim();
            return "(nessun dettaglio nel log)";
        }
    }
}
