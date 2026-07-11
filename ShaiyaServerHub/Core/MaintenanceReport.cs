using System;
using System.Data.SqlClient;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Stato sintetico della "Modalita Manutenzione" per la card Dashboard: legge
    /// PS_UserData.dbo.MaintenanceMode (Enabled/ChangedAt/ChangedBy). La tabella e il gate
    /// nel login (usp_Try_GameLogin_Taiwan) sono creati MANUALMENTE dall'admin (guida install
    /// SQL, non dall'Hub) — se la tabella non esiste ancora, <see cref="HasTable"/> è false
    /// e la card lo segnala invece di mostrare uno stato normale fuorviante.
    /// Connessione SEMPRE Windows auth (come <see cref="PsmCmdRepository"/>): l'Hub gira sul
    /// server come Administrator (=sysadmin). Query lenta relativamente (rete/DB) → chiamare
    /// da <see cref="MaintenanceReport.Load"/> off-thread.
    /// </summary>
    public class MaintenanceReport
    {
        public bool HasTable;
        public bool Enabled;
        public DateTime? ChangedAt;
        public string ChangedBy;

        // Conteggio live online (indipendente dal toggle manutenzione — utile anche a riposo
        // per capire chi si buttebbe fuori). Stessa definizione admin del gate login e di
        // GetOnlineNormalPlayerUIDs: Users_Master.Status IN (16,32,48,64,80).
        public bool CountsOk;
        public int AdminOnline;
        public int NormalOnline;

        public static MaintenanceReport Load(HubSettings settings)
        {
            var r = new MaintenanceReport();
            try
            {
                var csb = new SqlConnectionStringBuilder
                {
                    DataSource = settings?.SqlServer ?? "localhost",
                    InitialCatalog = "PS_UserData",
                    IntegratedSecurity = true,          // sempre Windows auth
                    TrustServerCertificate = true,
                    ConnectTimeout = 5,
                    ApplicationName = "ShaiyaServerHub"
                };

                using (var c = new SqlConnection(csb.ConnectionString))
                {
                    c.Open();
                    using (var cmd = new SqlCommand(
                        "IF OBJECT_ID('dbo.MaintenanceMode') IS NULL SELECT CAST(0 AS bit) AS HasTable, CAST(0 AS bit) AS Enabled, CAST(NULL AS datetime) AS ChangedAt, CAST(NULL AS nvarchar(128)) AS ChangedBy " +
                        "ELSE SELECT CAST(1 AS bit) AS HasTable, Enabled, ChangedAt, ChangedBy FROM dbo.MaintenanceMode;", c))
                    using (var rd = cmd.ExecuteReader())
                    {
                        if (rd.Read())
                        {
                            r.HasTable = (bool)rd["HasTable"];
                            if (r.HasTable)
                            {
                                r.Enabled = (bool)rd["Enabled"];
                                r.ChangedAt = rd["ChangedAt"] as DateTime?;
                                r.ChangedBy = rd["ChangedBy"] as string;
                            }
                        }
                    }

                    try
                    {
                        using (var cmd = new SqlCommand(
                            "SELECT " +
                            "  SUM(CASE WHEN u.[Status] IN (16,32,48,64,80) THEN 1 ELSE 0 END) AS AdminOnline, " +
                            "  SUM(CASE WHEN u.[Status] NOT IN (16,32,48,64,80) THEN 1 ELSE 0 END) AS NormalOnline " +
                            "FROM [PS_GameData].[dbo].[Chars] c " +
                            "JOIN [PS_UserData].[dbo].[Users_Master] u ON u.UserUID = c.UserUID " +
                            "WHERE c.LoginStatus = 1 AND c.Del = 0;", c))
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                r.AdminOnline = rd["AdminOnline"] as int? ?? 0;
                                r.NormalOnline = rd["NormalOnline"] as int? ?? 0;
                                r.CountsOk = true;
                            }
                        }
                    }
                    catch { /* CountsOk resta false: card mostra "–" per i conteggi */ }
                }
            }
            catch { /* HasTable resta false: card mostra "non disponibile" */ }
            return r;
        }
    }
}
