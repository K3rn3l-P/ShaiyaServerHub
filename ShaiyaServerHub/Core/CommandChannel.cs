using System;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Helper per il canale comandi PSM_Cmd. In particolare registra il login
    /// corrente (quello con cui il Hub si connette a SQL) in
    /// PSM_Cmd.dbo.GmCallerProfile con tier ECONOMY (rank 3), così anche in
    /// Windows auth (-E / sysadmin) gli script che usano usp_RunCommand
    /// (Auto-Boss /mmake, Auto-EXP /exp2xenable) non vengono negati (-3).
    /// </summary>
    public static class CommandChannel
    {
        public static int RegisterCurrentLoginAsEconomy(ScriptRunner runner, Action<string> log)
        {
            const string sql = @"
SET NOCOUNT ON;
DECLARE @l SYSNAME = SUSER_SNAME();
IF DB_ID('PSM_Cmd') IS NULL
BEGIN
    RAISERROR('PSM_Cmd non esiste su questo server.',16,1);
    RETURN;
END
MERGE PSM_Cmd.dbo.GmCallerProfile AS t
USING (SELECT @l AS CallerLogin) s ON t.CallerLogin = s.CallerLogin
WHEN MATCHED THEN UPDATE SET MaxTierRank = 3
WHEN NOT MATCHED THEN INSERT (CallerLogin, MaxTierRank) VALUES (@l, 3);
PRINT 'Login registrato nel canale comandi (ECONOMY): ' + @l;";
            string outp;
            int rc = runner.RunSqlQuery(sql, out outp, log);
            return rc;
        }
    }
}
