using System;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Percorso SQL SEPARATO per l'AI: usa SEMPRE il login dedicato read-only (ShaiyaAiReader, vedi
    /// script guida MSSQL), MAI le credenziali principali del Hub (ShaiyaTaskAgent/altri). Difesa in
    /// profondità: anche con un bug nel PolicyGate/AgentBrain, questo codice non può fisicamente
    /// scrivere — il login lato server non ha permessi di scrittura (db_datareader/MioRuoloRead). In
    /// più, come rete di sicurezza lato client, rifiuta qualsiasi query non SELECT/WITH o che contenga
    /// parole chiave di scrittura/esecuzione.
    /// </summary>
    public static class AiSqlReader
    {
        private static readonly Regex Forbidden = new Regex(
            @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|CREATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|sp_executesql)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Stesso formato colonne/separatore di ScriptRunner.FmtArgs, per coerenza dell'output.
        private const string FmtArgs = " -W -w 4000 -s \"|\" -I";

        /// <summary>Esegue una query SELECT-only su un DB specifico col login ShaiyaAiReader. Mai scrive.</summary>
        public static int Query(HubSettings settings, string database, string tsql, out string output)
        {
            string trimmed = (tsql ?? "").TrimStart();
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                output = "RIFIUTATO: solo query SELECT/WITH sono permesse su questo canale (identità AI read-only).";
                return -3;
            }
            if (Forbidden.IsMatch(tsql ?? ""))
            {
                output = "RIFIUTATO: query contiene parole chiave non permesse (scrittura/esecuzione).";
                return -3;
            }
            if (string.IsNullOrWhiteSpace(settings?.AiSqlReadPass))
            {
                output = "Login AI (ShaiyaAiReader) non configurato: imposta la password nelle Impostazioni AI " +
                          "dopo aver eseguito lo script di creazione login (vedi guida MSSQL).";
                return -2;
            }

            string db = string.IsNullOrWhiteSpace(database) ? "master" : database;
            string args = $"-S {Quote(settings.SqlServer)} -U {Quote(settings.AiSqlReadUser)} -P {Quote(settings.AiSqlReadPass)}" +
                          FmtArgs + $" -d {Quote(db)} -b -Q {Quote(tsql)}";
            return ProcessRunner.RunCapture("sqlcmd", args, out output);
        }

        private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";
    }
}
