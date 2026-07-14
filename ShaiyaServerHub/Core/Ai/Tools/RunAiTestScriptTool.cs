using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Esegue uno script scritto da <see cref="WriteAiTestScriptTool"/> — SEMPRE tramite
    /// <see cref="AiSqlReader"/> (login read-only ShaiyaAiReader, stesso canale di sql_read_query), MAI
    /// tramite <see cref="ScriptRunner"/> (che per la Test Suite reale gira in Windows auth/sysadmin).
    /// T0 come sql_read_query: il rischio è identico per costruzione (SELECT-only enforced lato client
    /// E lato server), cambia solo che la query arriva da un file persistito invece che inline.
    /// </summary>
    public class RunAiTestScriptTool : IAiTool
    {
        private readonly HubSettings _settings;

        public string Name => "run_ai_test_script";
        public string Description => "Esegue uno script SQL scritto in precedenza da write_ai_test_script (area " +
            "ai\\scripts\\sql\\, MAI la Test Suite di produzione) — tramite il login read-only ShaiyaAiReader " +
            "(stesso canale di sql_read_query, stesse restrizioni). 'database' = il DB su cui girare la query " +
            "(es. PS_UserData, PS_GameData). Read-only per costruzione, tier T0.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"name\":{\"type\":\"string\",\"description\":\"Nome dello script (senza estensione), vedi write_ai_test_script\"}," +
            "\"database\":{\"type\":\"string\",\"description\":\"Database su cui eseguire la query\"}}," +
            "\"required\":[\"name\",\"database\"]}";

        public RunAiTestScriptTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string name = null, database = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                    if (root.TryGetProperty("database", out var d) && d.ValueKind == JsonValueKind.String) database = d.GetString();
                }

                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return "RIFIUTATO: nome script mancante o non valido.";

                string path = Path.Combine(AiPaths.ScriptsSqlDir, name + ".sql");
                if (!File.Exists(path))
                    return $"ERRORE: script '{name}.sql' non trovato in ai\\scripts\\sql\\ — usa write_ai_test_script per crearlo prima.";

                // Rimuove le righe di commento (header aggiunto da write_ai_test_script) prima di eseguire.
                string sql = string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("--")));

                // AiSqlReader ri-applica comunque SELECT-only/parole vietate (difesa in profondità: il
                // file potrebbe in teoria essere stato editato a mano da qualcuno dopo la scrittura).
                int rc = AiSqlReader.Query(_settings, database, sql, out string output);
                return rc == 0
                    ? $"'{name}.sql' eseguito su {database}:\n{output}"
                    : $"Esito non OK (rc={rc}) — output:\n{output}";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
