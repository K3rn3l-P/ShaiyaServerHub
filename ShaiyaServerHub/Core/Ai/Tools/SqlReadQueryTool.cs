using System;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: query SQL in sola lettura sui database di gioco, tramite il login dedicato read-only
    /// (ShaiyaAiReader, vedi <see cref="AiSqlReader"/>) — identità SEPARATA da quella del Hub.
    /// </summary>
    public class SqlReadQueryTool : IAiTool
    {
        private readonly HubSettings _settings;
        public string Name => "sql_read_query";
        public string Description => "Esegue una query SQL in sola lettura (SOLO SELECT/WITH) su uno dei " +
            "database di gioco (PS_UserData, PS_GameData, PS_GameDefs, PS_GameLog, PS_ChatLog, PS_Billing, " +
            "PS_WebSite, PSM_Cmd) usando il login dedicato read-only ShaiyaAiReader. Non può scrivere né " +
            "eseguire procedure. Utile per leggere SecurityEvents, LoginAttempts, GmCommandLog, ecc.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"database\":{\"type\":\"string\",\"description\":\"Nome del database, es. PS_UserData\"}," +
            "\"query\":{\"type\":\"string\",\"description\":\"Query SELECT/WITH T-SQL\"}}," +
            "\"required\":[\"database\",\"query\"]}";

        public SqlReadQueryTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string db = null, query = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    if (doc.RootElement.TryGetProperty("database", out var d)) db = d.GetString();
                    if (doc.RootElement.TryGetProperty("query", out var q)) query = q.GetString();
                }
                if (string.IsNullOrWhiteSpace(query)) return "ERRORE: parametro 'query' mancante.";

                string output;
                int rc = AiSqlReader.Query(_settings, db, query, out output);
                return rc == 0 ? output : $"[exit {rc}]\n{output}";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
