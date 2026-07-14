using System;
using System.Text.Json;
using ShaiyaServerHub.Core.Ai.Sensors;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: legge il SQL Server Audit nativo (modifiche di schema + login SQL falliti a livello
    /// server) — "config-drift" del database, colma un gap che nessun sensore esistente copriva (le
    /// tabelle applicative SecurityEvents/LoginAttempts vedono solo il login DI GIOCO, non tentativi
    /// diretti sulla porta SQL 1433 contro login come 'sa'/'ojkndsaouin'; nessun sensore vedeva modifiche
    /// di schema come CREATE/ALTER/DROP di tabelle/procedure). Richiede che l'audit sia installato — vedi
    /// testsuite\sql\23_sql_audit_install.sql (Test Suite, bottone "Installa/Verifica").
    /// </summary>
    public class SqlAuditTool : IAiTool
    {
        public string Name => "sql_audit";
        public string Description => "Legge il SQL Server Audit nativo (server audit installato tramite Test Suite): " +
            "'schema_changes' = modifiche CREATE/ALTER/DROP su tabelle/procedure/viste in QUALSIASI database — " +
            "config-drift del database, un evento raro e sempre degno di attenzione, corrobora con chi l'ha fatto " +
            "(server_principal_name: se è l'admin noto è probabilmente manutenzione voluta, altrimenti allarma). " +
            "'failed_logins' = tentativi di login SQL falliti A LIVELLO SERVER (qualsiasi login, non solo l'account " +
            "di gioco — diverso da security_telemetry che vede solo login di gioco), raggruppati per IP. 'summary' " +
            "(default) = entrambi. 'minutes' = finestra temporale (default 60, max 1440).";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"focus\":{\"type\":\"string\",\"enum\":[\"summary\",\"schema_changes\",\"failed_logins\"]}," +
            "\"minutes\":{\"type\":\"integer\",\"description\":\"Finestra temporale in minuti, default 60, max 1440\"}}}";

        public string Execute(string argumentsJson)
        {
            try
            {
                string focus = "summary";
                int minutes = 60;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("focus", out var f) && f.ValueKind == JsonValueKind.String) focus = f.GetString();
                    if (root.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mv)) minutes = mv;
                }
                minutes = Math.Max(5, Math.Min(1440, minutes));

                switch ((focus ?? "summary").ToLowerInvariant())
                {
                    case "schema_changes":
                        SqlAuditSensor.SchemaChanges(minutes, out var schemaOut);
                        return $"Modifiche schema DB ultimi {minutes} min:\n{schemaOut}";

                    case "failed_logins":
                        SqlAuditSensor.FailedSqlLogins(minutes, out var loginOut);
                        return $"Login SQL falliti (livello server) ultimi {minutes} min, per IP:\n{loginOut}";

                    default:
                        SqlAuditSensor.SchemaChanges(minutes, out var schemaSum);
                        SqlAuditSensor.FailedSqlLogins(minutes, out var loginSum);
                        return $"Riepilogo SQL Audit (ultimi {minutes} min):\n\n" +
                               $"== Modifiche schema ==\n{schemaSum}\n" +
                               $"== Login SQL falliti per IP ==\n{loginSum}";
                }
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
