using System;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Esecutore SQL dedicato per il tool T3 "ban_player" — chiama DIRETTAMENTE
    /// PS_UserData.dbo.sp_BanUser (motivo+durata+GM tracciati, a differenza dell'UPDATE
    /// senza audit usato dal sito) con il login least-priv ShaiyaAiBanExecutor (SOLO
    /// EXECUTE su quella procedura, vedi 3.1.Login-BanExecutor-AI.sql). Percorso SEPARATO
    /// sia da ShaiyaAiReader (sola lettura) sia da ShaiyaTaskAgent/PSM_Cmd (comandi slash) —
    /// coerente con la convenzione del progetto di login dedicati per componente.
    /// </summary>
    public static class AiBanExecutor
    {
        public static int Execute(HubSettings settings, int userUid, int daysBann, string reason, out string output)
        {
            output = null;
            if (settings == null || string.IsNullOrWhiteSpace(settings.AiBanSqlPass))
            {
                output = "Login ban (ShaiyaAiBanExecutor) non configurato: imposta la password nelle Impostazioni AI dopo aver eseguito lo script 3.1.Login-BanExecutor-AI.sql.";
                return -2;
            }

            string safeReason = (reason ?? "").Replace("'", "''");
            string sql = $"EXEC dbo.sp_BanUser @UserUID={userUid}, @DaysBann={daysBann}, @Reason=N'{safeReason}', @GMID=N'ShaiyaHub-AI';";
            string args = $"-S {Quote(settings.SqlServer)} -U {Quote(settings.AiBanSqlUser)} -P {Quote(settings.AiBanSqlPass)} -W -w 4000 -s \"|\" -I -d PS_UserData -b -Q {Quote(sql)}";
            return ProcessRunner.RunCapture("sqlcmd", args, out output);
        }

        private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";
    }
}
