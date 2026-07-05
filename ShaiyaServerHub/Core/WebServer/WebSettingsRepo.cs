using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core.WebServer
{
    /// <summary>Riga chiave/valore di Web_Settings (con categoria per il raggruppamento UI).</summary>
    public class WebSetting
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string Group { get; set; } = "Altri";
        public bool IsJson { get; set; }        // valore atteso JSON → validato prima del salvataggio
        public bool ReadOnly { get; set; }      // gestito dal sistema (heartbeat, secret): non editare a mano
    }

    /// <summary>
    /// Lettura/scrittura di PS_WebSite.dbo.Web_Settings dall'Hub, via una connessione dedicata al SITO
    /// (Ernoweb@) — separata da quella di gioco. Auth da HubSettings.WebSqlAuthMode
    /// (Env = variabili DB_USER/DB_PASSWORD come il sito, default / Windows / Sql). Solo sqlcmd, no HTTP.
    /// </summary>
    public static class WebSettingsRepo
    {
        // ── Connessione al DB del sito ───────────────────────
        public static ScriptRunner WebRunner(HubSettings s)
        {
            string webDb = WebDbName(s);
            var ws = new HubSettings
            {
                SqlServer = string.IsNullOrWhiteSpace(s.SqlServer) ? "localhost" : s.SqlServer,
                DefaultDatabase = webDb,
            };
            string mode = (s.WebSqlAuthMode ?? "Env").Trim();
            if (mode.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                ws.SqlAuthMode = "Windows";
            else if (mode.Equals("Sql", StringComparison.OrdinalIgnoreCase))
            {
                ws.SqlAuthMode = "Sql"; ws.SqlUser = s.WebSqlUser; ws.SqlPassword = s.WebSqlPassword;
            }
            else // Env (default): come il sito.
            {
                ws.SqlAuthMode = "Sql";
                ws.SqlUser = Env("DB_USER");
                ws.SqlPassword = Env("DB_PASSWORD");
                string host = Env("DB_HOST");
                if (!string.IsNullOrWhiteSpace(host)) ws.SqlServer = host;
                string envDb = Env("DB_WEBSITE_NAME");
                if (!string.IsNullOrWhiteSpace(envDb) && Regex.IsMatch(envDb, "^[A-Za-z0-9_]+$")) ws.DefaultDatabase = envDb;
            }
            return new ScriptRunner(ws);
        }

        private static string Env(string name) =>
            Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
            ?? Environment.GetEnvironmentVariable(name) ?? "";

        private static string WebDbName(HubSettings s)
        {
            string d = (s?.WebsiteDatabase ?? "").Trim();
            return (d.Length > 0 && Regex.IsMatch(d, "^[A-Za-z0-9_]+$")) ? d : "PS_WebSite";
        }
        private static string Db(HubSettings s) => "[" + WebDbName(s) + "]";

        // ── Letture/scritture ────────────────────────────────
        public static List<WebSetting> ListSettings(HubSettings s)
        {
            var web = WebRunner(s);
            string db = Db(s);
            // valore in HEX (UTF-16LE): può contenere '|' e newline che romperebbero il parser pipe.
            var rows = Rows(web, $@"SELECT SettingKey,
                CONVERT(VARCHAR(MAX), CONVERT(VARBINARY(MAX), CAST(SettingValue AS NVARCHAR(MAX))), 2) AS hv
                FROM {db}.dbo.Web_Settings ORDER BY SettingKey;");
            var list = new List<WebSetting>();
            foreach (var c in rows)
            {
                if (c.Length < 2) continue;
                string key = c[0].Trim();
                list.Add(new WebSetting
                {
                    Key = key,
                    Value = FromHexUtf16(c[1].Trim()),
                    Group = CategoryOf(key),
                    IsJson = key.EndsWith("_Permissions", StringComparison.OrdinalIgnoreCase),
                    ReadOnly = key.Equals("TaskSchedulerLastHeartbeat", StringComparison.OrdinalIgnoreCase)
                            || key.Equals("TaskSchedulerWorkerSecret", StringComparison.OrdinalIgnoreCase)
                });
            }
            return list;
        }

        /// <summary>
        /// Salva un'impostazione. Il valore è memorizzato COME TESTO grezzo (il sito conserva i JSON come
        /// stringa e li rilegge con json_decode). Per le chiavi JSON (*_Permissions) valida la sintassi
        /// prima di scrivere. Ritorna null se ok, altrimenti il messaggio d'errore.
        /// </summary>
        public static string SaveSetting(HubSettings s, string key, string value, Action<string> log)
        {
            if (key.EndsWith("_Permissions", StringComparison.OrdinalIgnoreCase) && !LooksLikeJson(value))
            {
                log?.Invoke($"✗ '{key}': JSON non valido, non salvato.");
                return "JSON non valido (parentesi/virgolette sbilanciate)";
            }
            var web = WebRunner(s);
            string db = Db(s);
            string k = Esc(key), v = Esc(value);
            web.RunSqlQuery($@"IF EXISTS(SELECT 1 FROM {db}.dbo.Web_Settings WHERE SettingKey=N'{k}')
   UPDATE {db}.dbo.Web_Settings SET SettingValue=N'{v}' WHERE SettingKey=N'{k}';
 ELSE INSERT INTO {db}.dbo.Web_Settings (SettingKey, SettingValue) VALUES (N'{k}', N'{v}');", out _, null);
            log?.Invoke($"✓ Setting '{key}' salvato");
            return null;
        }

        private static string CategoryOf(string key)
        {
            string k = key ?? "";
            if (k.StartsWith("Drops", StringComparison.OrdinalIgnoreCase)) return "Drops";
            if (k.EndsWith("_Permissions", StringComparison.OrdinalIgnoreCase)) return "Permessi";
            if (k.StartsWith("Lucky", StringComparison.OrdinalIgnoreCase)) return "Lucky Chest";
            if (k.StartsWith("TaskScheduler", StringComparison.OrdinalIgnoreCase)) return "Scheduler";
            return "Altri";
        }

        // Validazione JSON leggera (zero deps): parentesi/virgolette bilanciate.
        private static bool LooksLikeJson(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length == 0) return false;
            if (!((v[0] == '[' && v[v.Length - 1] == ']') || (v[0] == '{' && v[v.Length - 1] == '}'))) return false;
            int sq = 0, cu = 0; bool inStr = false; char prev = '\0';
            foreach (char c in v)
            {
                if (inStr) { if (c == '"' && prev != '\\') inStr = false; }
                else if (c == '"') inStr = true;
                else if (c == '[') sq++;
                else if (c == ']') sq--;
                else if (c == '{') cu++;
                else if (c == '}') cu--;
                if (sq < 0 || cu < 0) return false;
                prev = c;
            }
            return sq == 0 && cu == 0 && !inStr;
        }

        // ── Parsing output sqlcmd (-W -s "|") → righe dati ───
        private static List<string[]> Rows(ScriptRunner r, string tsql)
        {
            var res = new List<string[]>();
            r.RunSqlQuery("SET NOCOUNT ON;\n" + tsql, out string outp, null);
            if (string.IsNullOrEmpty(outp)) return res;
            bool pastSep = false;
            foreach (var raw in outp.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (!pastSep)
                {
                    if (line.IndexOf('-') >= 0 && Regex.IsMatch(line, @"^[-|\s]+$")) pastSep = true;
                    continue;
                }
                if (line.Length == 0) continue;
                res.Add(line.Split('|'));
            }
            return res;
        }

        private static string FromHexUtf16(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0) return "";
            try
            {
                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                return Encoding.Unicode.GetString(bytes);
            }
            catch { return ""; }
        }

        private static string Esc(string s) => (s ?? "").Replace("'", "''");
    }
}
