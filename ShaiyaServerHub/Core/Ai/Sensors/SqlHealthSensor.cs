namespace ShaiyaServerHub.Core.Ai.Sensors
{
    /// <summary>
    /// Salute SQL Server oltre "servizio Running" — blocking/tempdb/query lunghe, via DMV. Query FISSE
    /// e verificate (stesso principio di <see cref="SecurityTelemetry"/>: mai SQL libero generato dal
    /// modello), eseguite col login read-only <see cref="AiSqlReader"/> (ShaiyaAiReader). Verificato sul
    /// server reale che il login ha GIÀ <c>VIEW SERVER STATE</c> (grant originale dello script
    /// 3.0.Login-ReadOnly-AI-Monitor.sql) — nessun nuovo grant necessario, le 3 query sotto girano senza
    /// errori di permesso. Limiti onesti (fuori scope): deadlock storici (richiederebbero Extended
    /// Events) e xp_readerrorlog (richiede sysadmin) non disponibili qui.
    /// </summary>
    public static class SqlHealthSensor
    {
        public static int BlockingChains(HubSettings settings, out string output)
        {
            string sql = "SELECT r.session_id AS BlockedSPID, r.blocking_session_id AS BlockingSPID, " +
                "DATEDIFF(SECOND, r.start_time, GETDATE()) AS WaitSeconds, r.wait_type, r.command " +
                "FROM sys.dm_exec_requests r WHERE r.blocking_session_id <> 0 ORDER BY WaitSeconds DESC;";
            return AiSqlReader.Query(settings, "master", sql, out output);
        }

        public static int TempDbUsage(HubSettings settings, out string output)
        {
            string sql = "SELECT SUM(unallocated_extent_page_count)*8/1024 AS FreeMB, " +
                "SUM(user_object_reserved_page_count)*8/1024 AS UserObjMB, " +
                "SUM(internal_object_reserved_page_count)*8/1024 AS InternalObjMB " +
                "FROM tempdb.sys.dm_db_file_space_usage;";
            return AiSqlReader.Query(settings, "master", sql, out output);
        }

        public static int LongRunningRequests(HubSettings settings, int minSeconds, out string output)
        {
            string sql = $"SELECT r.session_id, DATEDIFF(SECOND, r.start_time, GETDATE()) AS ElapsedSeconds, " +
                "r.status, r.command, SUBSTRING(t.text,1,200) AS QueryText " +
                "FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t " +
                $"WHERE DATEDIFF(SECOND, r.start_time, GETDATE()) > {minSeconds} AND r.session_id <> @@SPID " +
                "ORDER BY ElapsedSeconds DESC;";
            return AiSqlReader.Query(settings, "master", sql, out output);
        }
    }
}
