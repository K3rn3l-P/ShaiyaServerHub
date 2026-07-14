using System;
using System.IO;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// "Testsuite proattiva" (ultimo pezzo di P3 dal piano originale) — l'AI può scrivere NUOVI
    /// controlli in un'area SEPARATA dalla Test Suite di produzione (ai\scripts\sql\, MAI
    /// scripts\testsuite\ — quella resta editabile solo dall'admin, vedi decisione presa per
    /// read_hub_file: mai scrittura su script che girano senza supervisione). Deliberatamente
    /// ristretto a SOLE query SELECT/WITH (stesso vincolo di sql_read_query/AiSqlReader): eseguito da
    /// <see cref="RunAiTestScriptTool"/> con lo STESSO login read-only ShaiyaAiReader, mai con l'auth
    /// elevata (Windows -E / sysadmin) usata dalla vera Test Suite (vedi MainWindow.xaml.cs:
    /// "TestArea.Runner = new ScriptRunner(..., forceWindowsAuth: true)") — un bug o un'allucinazione
    /// in uno script AI-generato non può comunque scrivere nulla, il login lato server non ne ha il
    /// permesso, indipendentemente da cosa contiene il file.
    /// T1 (non T0): scrivere un nuovo file è comunque una mutazione, anche se innocua — reversibile
    /// (basta cancellare il file a mano), mai sovrascrive uno script esistente.
    /// </summary>
    public class WriteAiTestScriptTool : IAiTool
    {
        public string Name => "write_ai_test_script";
        public string Description => "Scrive un NUOVO script di controllo in ai\\scripts\\sql\\ (area separata dalla " +
            "Test Suite di produzione, non la tocca mai) per coprire una superficie non ancora testata. Lo script " +
            "DEVE essere una singola query SELECT/WITH (stesso vincolo di sql_read_query) che ritorna colonne tipo " +
            "Status/Detail (PASS/WARN/FAIL) — verrà eseguito con lo STESSO login read-only ShaiyaAiReader (mai " +
            "privilegi elevati), quindi non può scrivere nulla anche se il contenuto fosse sbagliato. 'name' solo " +
            "lettere/numeri/trattini (niente estensione, aggiunta automaticamente), MAI sovrascrive uno script " +
            "esistente. Azione T1, reversibile.";
        public AiToolTier Tier => AiToolTier.T1_AutoHeal;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"name\":{\"type\":\"string\",\"description\":\"Nome file (senza estensione), solo lettere/numeri/trattini\"}," +
            "\"purpose\":{\"type\":\"string\",\"description\":\"Cosa verifica questo controllo (breve, salvato come commento)\"}," +
            "\"sql\":{\"type\":\"string\",\"description\":\"Query SELECT/WITH che ritorna Status (PASS/WARN/FAIL) + Detail\"}}," +
            "\"required\":[\"name\",\"purpose\",\"sql\"]}";

        public string Execute(string argumentsJson)
        {
            try
            {
                string name = null, purpose = null, sql = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                    if (root.TryGetProperty("purpose", out var p) && p.ValueKind == JsonValueKind.String) purpose = p.GetString();
                    if (root.TryGetProperty("sql", out var s) && s.ValueKind == JsonValueKind.String) sql = s.GetString();
                }

                // Ri-verifica anche qui (difesa in profondità, come per gli altri tool T1+).
                if (!Invariants.CheckWriteAiTestScript(name, sql, out var denyReason))
                    return "RIFIUTATO: " + denyReason;

                Directory.CreateDirectory(AiPaths.ScriptsSqlDir);
                string path = Path.Combine(AiPaths.ScriptsSqlDir, name + ".sql");
                if (File.Exists(path))
                    return $"RIFIUTATO: '{name}.sql' esiste già — mai sovrascritto, scegli un nome diverso.";

                string content = $"-- {purpose}\n" +
                    $"-- Scritto dall'AI (Hub Brain) il {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC — SOLO SELECT/WITH, " +
                    "eseguito con login read-only ShaiyaAiReader (mai privilegi elevati).\n" + sql + "\n";
                File.WriteAllText(path, content);

                return $"Script '{name}.sql' scritto in ai\\scripts\\sql\\. Eseguilo con " +
                       $"run_ai_test_script({{\"name\":\"{name}\",\"database\":\"...\"}}) per verificarlo.";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
