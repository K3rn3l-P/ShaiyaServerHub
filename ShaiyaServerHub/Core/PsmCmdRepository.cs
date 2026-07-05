using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Accesso dati ADO.NET al DB PSM_Cmd (allowlist, profili chiamante, audit log).
    /// Query SEMPRE parametrizzate. Connessione in Integrated Security (Windows auth):
    /// l'Hub gira sul server come Administrator (=sysadmin), unico modo per leggere/scrivere
    /// gli oggetti least-privilege che l'account SQL principale (ShaiyaTaskAgent) non vede.
    /// </summary>
    public class PsmCmdRepository
    {
        private readonly string _connStr;

        public PsmCmdRepository(HubSettings settings)
        {
            var csb = new SqlConnectionStringBuilder
            {
                DataSource = settings?.SqlServer ?? "localhost",
                InitialCatalog = "PSM_Cmd",
                IntegratedSecurity = true,          // sempre Windows auth (come la sezione Funzioni)
                TrustServerCertificate = true,
                ConnectTimeout = 5,
                ApplicationName = "ShaiyaServerHub"
            };
            _connStr = csb.ConnectionString;
        }

        private SqlConnection Open()
        {
            var c = new SqlConnection(_connStr);
            c.Open();
            return c;
        }

        // ── Allowlist ────────────────────────────────────────
        public List<AllowlistRow> LoadAllowlist()
        {
            var list = new List<AllowlistRow>();
            using (var c = Open())
            using (var cmd = new SqlCommand(
                "SELECT Command, Service, Tier, Enabled, Description " +
                "FROM dbo.GmCommandAllowlist ORDER BY Service, Tier, Command", c))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    list.Add(new AllowlistRow
                    {
                        Command = r.GetString(0),
                        Service = r.GetString(1),
                        Tier = r.GetString(2),
                        Enabled = r.GetBoolean(3),
                        Description = r.IsDBNull(4) ? null : r.GetString(4)
                    });
            }
            return list;
        }

        /// <summary>Salva Tier/Enabled/Description delle righe (PK Command+Service). Transazionale.</summary>
        public int SaveAllowlist(IEnumerable<AllowlistRow> rows)
        {
            int n = 0;
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    foreach (var row in rows)
                    {
                        using (var cmd = new SqlCommand(
                            "UPDATE dbo.GmCommandAllowlist SET Tier=@t, Enabled=@e, Description=@d " +
                            "WHERE Command=@c AND Service=@s", c, tx))
                        {
                            cmd.Parameters.Add("@t", SqlDbType.VarChar, 10).Value = row.Tier ?? "SAFE";
                            cmd.Parameters.Add("@e", SqlDbType.Bit).Value = row.Enabled;
                            cmd.Parameters.Add("@d", SqlDbType.NVarChar, 200).Value =
                                (object)row.Description ?? DBNull.Value;
                            cmd.Parameters.Add("@c", SqlDbType.NVarChar, 64).Value = row.Command;
                            cmd.Parameters.Add("@s", SqlDbType.NVarChar, 20).Value = row.Service;
                            n += cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
            return n;
        }

        // ── Profili chiamante ────────────────────────────────
        public List<CallerProfileRow> LoadCallerProfiles()
        {
            var list = new List<CallerProfileRow>();
            using (var c = Open())
            using (var cmd = new SqlCommand(
                "SELECT CallerLogin, MaxTierRank FROM dbo.GmCallerProfile ORDER BY CallerLogin", c))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    list.Add(new CallerProfileRow
                    {
                        CallerLogin = r.GetString(0),
                        MaxTierRank = r.GetByte(1)
                    });
            }
            return list;
        }

        /// <summary>
        /// Riconcilia GmCallerProfile con la lista fornita: upsert dei presenti,
        /// DELETE di quelli non più presenti. Transazionale. Ritorna righe upsertate.
        /// </summary>
        public int SaveCallerProfiles(IEnumerable<CallerProfileRow> rows)
        {
            var wanted = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.CallerLogin))
                .GroupBy(r => r.CallerLogin.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())   // ultima vince in caso di duplicati
                .ToList();

            foreach (var r in wanted)
                if (r.MaxTierRank < 1 || r.MaxTierRank > 3)
                    throw new InvalidOperationException(
                        $"Tier non valido per '{r.CallerLogin}': ammessi SAFE/PLAYER/ECONOMY (1..3).");

            int n = 0;
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    // 1) DELETE dei login non più presenti.
                    var keep = wanted.Select(r => r.CallerLogin.Trim()).ToList();
                    var existing = new List<string>();
                    using (var cmd = new SqlCommand("SELECT CallerLogin FROM dbo.GmCallerProfile", c, tx))
                    using (var rd = cmd.ExecuteReader())
                        while (rd.Read()) existing.Add(rd.GetString(0));

                    foreach (var login in existing.Where(e =>
                        !keep.Any(k => string.Equals(k, e, StringComparison.OrdinalIgnoreCase))))
                    {
                        using (var del = new SqlCommand(
                            "DELETE FROM dbo.GmCallerProfile WHERE CallerLogin=@l", c, tx))
                        {
                            del.Parameters.Add("@l", SqlDbType.NVarChar, 128).Value = login;
                            del.ExecuteNonQuery();
                        }
                    }

                    // 2) Upsert (MERGE) dei voluti.
                    foreach (var r in wanted)
                    {
                        using (var cmd = new SqlCommand(
                            "MERGE dbo.GmCallerProfile AS t " +
                            "USING (SELECT @l AS CallerLogin) s ON t.CallerLogin=s.CallerLogin " +
                            "WHEN MATCHED THEN UPDATE SET MaxTierRank=@r " +
                            "WHEN NOT MATCHED THEN INSERT (CallerLogin, MaxTierRank) VALUES (@l, @r);",
                            c, tx))
                        {
                            cmd.Parameters.Add("@l", SqlDbType.NVarChar, 128).Value = r.CallerLogin.Trim();
                            cmd.Parameters.Add("@r", SqlDbType.TinyInt).Value = r.MaxTierRank;
                            n += cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
            return n;
        }

        // ── Audit log ────────────────────────────────────────
        /// <summary>Carica le ultime righe di log (più recenti prima), con filtri opzionali.</summary>
        public List<CommandLogRow> LoadLog(int topN, string result = null, string wrapper = null, string search = null)
        {
            var list = new List<CommandLogRow>();
            var sql =
                "SELECT TOP (@top) Id, Ts, CallerLogin, Wrapper, Service, Command, Result " +
                "FROM dbo.GmCommandLog WHERE 1=1";
            if (!string.IsNullOrEmpty(result)) sql += " AND Result=@res";
            if (!string.IsNullOrEmpty(wrapper)) sql += " AND Wrapper=@wrap";
            if (!string.IsNullOrWhiteSpace(search)) sql += " AND Command LIKE @q";
            sql += " ORDER BY Id DESC";

            using (var c = Open())
            using (var cmd = new SqlCommand(sql, c))
            {
                cmd.Parameters.Add("@top", SqlDbType.Int).Value = topN;
                if (!string.IsNullOrEmpty(result)) cmd.Parameters.Add("@res", SqlDbType.VarChar, 20).Value = result;
                if (!string.IsNullOrEmpty(wrapper)) cmd.Parameters.Add("@wrap", SqlDbType.VarChar, 20).Value = wrapper;
                if (!string.IsNullOrWhiteSpace(search))
                    cmd.Parameters.Add("@q", SqlDbType.NVarChar, 4000).Value = "%" + search.Trim() + "%";

                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new CommandLogRow
                        {
                            Id = r.GetInt64(0),
                            Ts = DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc).ToLocalTime(),
                            CallerLogin = r.IsDBNull(2) ? null : r.GetString(2),
                            Wrapper = r.GetString(3),
                            Service = r.IsDBNull(4) ? null : r.GetString(4),
                            Command = r.IsDBNull(5) ? null : r.GetString(5),
                            Result = r.GetString(6)
                        });
            }
            return list;
        }

        public int LogCount()
        {
            using (var c = Open())
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.GmCommandLog", c))
                return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Svuota l'audit log. Se olderThanDays &gt; 0 cancella solo le righe più vecchie di N giorni,
        /// altrimenti TUTTE. Ritorna il numero di righe cancellate.
        /// </summary>
        public int ClearLog(int olderThanDays = 0)
        {
            using (var c = Open())
            {
                SqlCommand cmd;
                if (olderThanDays > 0)
                {
                    cmd = new SqlCommand(
                        "DELETE FROM dbo.GmCommandLog WHERE Ts < DATEADD(DAY, -@d, SYSUTCDATETIME())", c);
                    cmd.Parameters.Add("@d", SqlDbType.Int).Value = olderThanDays;
                }
                else
                {
                    cmd = new SqlCommand("DELETE FROM dbo.GmCommandLog", c);
                }
                using (cmd) return cmd.ExecuteNonQuery();
            }
        }
    }
}
