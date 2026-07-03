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

        /// <summary>
        /// Verifica strutturale che PSM_Cmd sia installato e completo (DB + wrapper +
        /// CLR Command + allowlist). Scrive il dettaglio nel log e ritorna true se attivo.
        /// </summary>
        public static bool VerifyPsmCmd(ScriptRunner runner, Action<string> log)
        {
            const string sql = @"
SET NOCOUNT ON;
DECLARE @db INT = CASE WHEN DB_ID('PSM_Cmd') IS NULL THEN 0 ELSE 1 END;
PRINT 'DB PSM_Cmd............: ' + CASE WHEN @db=1 THEN 'OK' ELSE 'MANCANTE' END;
IF @db=1
BEGIN
    PRINT 'usp_SendNotice.......: ' + CASE WHEN OBJECT_ID('PSM_Cmd.dbo.usp_SendNotice') IS NULL THEN 'MANCANTE' ELSE 'OK' END;
    PRINT 'usp_RunCommand.......: ' + CASE WHEN OBJECT_ID('PSM_Cmd.dbo.usp_RunCommand') IS NULL THEN 'MANCANTE' ELSE 'OK' END;
    PRINT 'dbo.Command (CLR)....: ' + CASE WHEN OBJECT_ID('PSM_Cmd.dbo.Command') IS NULL THEN 'MANCANTE' ELSE 'OK' END;
    PRINT 'GmCommandAllowlist...: ' + CASE WHEN OBJECT_ID('PSM_Cmd.dbo.GmCommandAllowlist') IS NULL THEN 'MANCANTE' ELSE 'OK' END;
    PRINT 'GmCallerProfile......: ' + CASE WHEN OBJECT_ID('PSM_Cmd.dbo.GmCallerProfile') IS NULL THEN 'MANCANTE' ELSE 'OK' END;
    PRINT 'GmCommandLog (audit).: ' + CASE WHEN OBJECT_ID('PSM_Cmd.dbo.GmCommandLog') IS NULL THEN 'MANCANTE' ELSE 'OK' END;
END
DECLARE @ok INT = CASE WHEN @db=1
    AND OBJECT_ID('PSM_Cmd.dbo.usp_SendNotice') IS NOT NULL
    AND OBJECT_ID('PSM_Cmd.dbo.usp_RunCommand') IS NOT NULL
    AND OBJECT_ID('PSM_Cmd.dbo.Command') IS NOT NULL
    AND OBJECT_ID('PSM_Cmd.dbo.GmCommandAllowlist') IS NOT NULL
    THEN 1 ELSE 0 END;
PRINT 'VERDETTO=' + CASE WHEN @ok=1 THEN 'ATTIVO' ELSE 'NON-ATTIVO' END;";
            string outp;
            int rc = runner.RunSqlQuery(sql, out outp, log);
            return rc == 0 && outp.IndexOf("VERDETTO=ATTIVO", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
