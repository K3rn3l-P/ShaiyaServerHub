namespace ShaiyaServerHub.Core.Ai.Sensors
{
    /// <summary>
    /// Query curate e READ-ONLY (via <see cref="AiSqlReader"/>/login <c>ShaiyaAiReader</c>) sulla
    /// telemetria di sicurezza NATIVA già presente sul server (vedi PLAN §Realtà del server —
    /// SecurityEvents/LoginAttempts/GameSecuritySessions, non sensori simulati). Schema colonne
    /// verificato via SSH sul server reale, non ipotesi. Stesso principio di <see cref="PlayerLookup"/>/
    /// <see cref="AiBanExecutor"/>: query T-SQL fisse e verificate una volta, non generate al volo dal
    /// modello — riduce il rischio che un modello debole scriva una query sbagliata su tabelle che non
    /// conosce a menadito.
    /// </summary>
    public static class SecurityTelemetry
    {
        // EventType ad altissimo volume/basso segnale — osservato sul server reale: RANGE_HACK_PVE da
        // solo ha migliaia di righe (falsi positivi noti EtainShield, vedi CLAUDE.md "defenses.md"),
        // PLAYER_ENTER/LEAVE_WORLD sono telemetria di sessione normale, non un incidente. Riassunti per
        // conteggio nel summary, esclusi dal dettaglio eventi per non affogare il modello in rumore.
        private const string NoisyEventTypesSql = "'RANGE_HACK_PVE','RANGE_HACK_PVE_INFO','PLAYER_ENTER_WORLD','PLAYER_LEAVE_WORLD'";

        public static int EventCountsByType(HubSettings settings, int minutes, out string output)
        {
            string sql = $"SELECT EventType, COUNT(*) AS N FROM dbo.SecurityEvents " +
                         $"WHERE EventTime >= DATEADD(MINUTE,-{minutes},GETDATE()) GROUP BY EventType ORDER BY N DESC;";
            return AiSqlReader.Query(settings, "PS_UserData", sql, out output);
        }

        public static int SignificantEvents(HubSettings settings, int minutes, out string output)
        {
            string sql = $"SELECT TOP 30 EventTime, Username, IP, EventType, Detail FROM dbo.SecurityEvents " +
                         $"WHERE EventTime >= DATEADD(MINUTE,-{minutes},GETDATE()) AND EventType NOT IN ({NoisyEventTypesSql}) " +
                         "ORDER BY EventTime DESC;";
            return AiSqlReader.Query(settings, "PS_UserData", sql, out output);
        }

        public static int FailedLoginsByIp(HubSettings settings, int minutes, out string output)
        {
            string sql = $"SELECT IPAddress, COUNT(*) AS Failures, MAX(AttemptTime) AS LastAttempt FROM dbo.LoginAttempts " +
                         $"WHERE Success=0 AND AttemptTime >= DATEADD(MINUTE,-{minutes},GETDATE()) " +
                         "GROUP BY IPAddress ORDER BY Failures DESC;";
            return AiSqlReader.Query(settings, "PS_UserData", sql, out output);
        }

        public static int ActiveSessionsByIp(HubSettings settings, out string output)
        {
            string sql = "SELECT ClientIP, COUNT(*) AS Sessions, MIN(CreatedAt) AS Oldest FROM dbo.GameSecuritySessions " +
                         "WHERE ExpiresAt > GETDATE() GROUP BY ClientIP ORDER BY Sessions DESC;";
            return AiSqlReader.Query(settings, "PS_UserData", sql, out output);
        }
    }
}
