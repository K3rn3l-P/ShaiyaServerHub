using System;
using System.Text.Json;
using ShaiyaServerHub.Core.Ai.Sensors;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>Tool T0: salute SQL Server profonda (blocking/tempdb/query lunghe) — vedi PLAN §P4, <see cref="SqlHealthSensor"/>.</summary>
    public class SqlHealthTool : IAiTool
    {
        private readonly HubSettings _settings;

        public string Name => "sql_health";
        public string Description => "Salute SQL Server oltre 'servizio Running': 'blocking' = catene di blocco " +
            "attive (chi blocca chi, da quanto), 'tempdb' = spazio tempdb libero/usato (tempdb pieno è un incidente " +
            "classico che il solo stato 'Running' non rileva), 'long_running' = query in esecuzione da oltre 30s, " +
            "'summary' (default) = tutto insieme. Read-only, query fisse via login ShaiyaAiReader.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"focus\":{\"type\":\"string\",\"enum\":[\"summary\",\"blocking\",\"tempdb\",\"long_running\"]}}}";

        public SqlHealthTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string focus = "summary";
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                    if (doc.RootElement.TryGetProperty("focus", out var f) && f.ValueKind == JsonValueKind.String) focus = f.GetString();

                switch ((focus ?? "summary").ToLowerInvariant())
                {
                    case "blocking":
                        SqlHealthSensor.BlockingChains(_settings, out var blockOut);
                        return $"Catene di blocco attive:\n{blockOut}";
                    case "tempdb":
                        SqlHealthSensor.TempDbUsage(_settings, out var tempOut);
                        return $"Uso tempdb:\n{tempOut}";
                    case "long_running":
                        SqlHealthSensor.LongRunningRequests(_settings, 30, out var longOut);
                        return $"Query in esecuzione da oltre 30s:\n{longOut}";
                    default:
                        SqlHealthSensor.BlockingChains(_settings, out var b);
                        SqlHealthSensor.TempDbUsage(_settings, out var t);
                        SqlHealthSensor.LongRunningRequests(_settings, 30, out var l);
                        return $"== Blocking ==\n{b}\n== TempDB ==\n{t}\n== Query lunghe (>30s) ==\n{l}";
                }
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
