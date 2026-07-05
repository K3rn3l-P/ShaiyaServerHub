using System;
using System.IO;

namespace ShaiyaServerHub.Core
{
    /// <summary>Esegue uno script (SQL via sqlcmd, PS1 via powershell, BAT via cmd).</summary>
    public class ScriptRunner
    {
        private readonly HubSettings _settings;
        // Se true, gli SQL girano SEMPRE in autenticazione Windows (-E), ignorando
        // SqlAuthMode/SqlUser delle impostazioni. Usato dalla sezione "Funzioni":
        // l'Hub è localhost sul server → -E = SYSTEM, che vede gli oggetti least-priv
        // di PSM_Cmd (dbo.Command CLR, Gm* tabelle) invisibili a un login SQL ristretto
        // come ShaiyaTaskAgent. Le impostazioni principali restano invariate.
        private readonly bool _forceWindowsAuth;

        public ScriptRunner(HubSettings settings) : this(settings, false) { }
        public ScriptRunner(HubSettings settings, bool forceWindowsAuth)
        {
            _settings = settings;
            _forceWindowsAuth = forceWindowsAuth;
        }

        public int RunScript(ScriptItem item, Action<string> log)
        {
            switch (item.Kind)
            {
                case ScriptKind.Sql: return RunSql(item.FullPath, log);
                case ScriptKind.PowerShell: return RunPowerShell(item.FullPath, log);
                case ScriptKind.Batch: return RunBatch(item.FullPath, log);
                default:
                    log?.Invoke("Tipo script non supportato: " + item.Name);
                    return -1;
            }
        }

        // ── SQL (sqlcmd) ─────────────────────────────────────
        // -W rimuove il padding a spazi, -w allarga la riga, -s separatore.
        // -I attiva QUOTED_IDENTIFIER (richiesto dai metodi XML .nodes()/.value()).
        private const string FmtArgs = " -W -w 4000 -s \"|\" -I";

        public int RunSql(string scriptPath, Action<string> log)
        {
            string args = BuildSqlcmdAuth() + FmtArgs +
                          $" -d {Quote(_settings.DefaultDatabase)} -b -i {Quote(scriptPath)}";
            string authLabel = _forceWindowsAuth ? "Windows(-E, forzata)" : _settings.SqlAuthMode;
            log?.Invoke($"[sqlcmd] {Path.GetFileName(scriptPath)}  (server={_settings.SqlServer}, auth={authLabel})");
            int rc = ProcessRunner.Run("sqlcmd", args, log);
            log?.Invoke(rc == 0 ? "✓ sqlcmd OK (exit 0)" : $"✗ sqlcmd exit {rc}");
            return rc;
        }

        /// <summary>Esegue una query inline (-Q) e restituisce l'output.</summary>
        public int RunSqlQuery(string tsql, out string output, Action<string> log = null)
        {
            string args = BuildSqlcmdAuth() + FmtArgs +
                          $" -d {Quote(_settings.DefaultDatabase)} -b -Q {Quote(tsql)}";
            int rc = ProcessRunner.RunCapture("sqlcmd", args, out output);
            if (log != null) foreach (var l in output.Split('\n')) log(l.TrimEnd());
            return rc;
        }

        private string BuildSqlcmdAuth()
        {
            string baseArgs = $"-S {Quote(_settings.SqlServer)}";
            if (!_forceWindowsAuth &&
                string.Equals(_settings.SqlAuthMode, "Sql", StringComparison.OrdinalIgnoreCase))
                return baseArgs + $" -U {Quote(_settings.SqlUser)} -P {Quote(_settings.SqlPassword)}";
            return baseArgs + " -E"; // Windows auth (forzata per la sezione Funzioni)
        }

        // ── PowerShell ───────────────────────────────────────
        public int RunPowerShell(string scriptPath, Action<string> log)
        {
            // I check della test suite ritornano un oggetto {Name,Status,Detail}. Con 3 proprietà
            // PowerShell lo formatterebbe come TABELLA; forziamo Format-List così esce "Status : PASS"
            // (riga parsabile per il badge). Gli script senza oggetto restano invariati.
            string p = scriptPath.Replace("'", "''");
            string args = "-NoProfile -ExecutionPolicy Bypass -Command \"& '" + p +
                          "' | Format-List | Out-String -Width 4000\"";
            log?.Invoke($"[powershell] {Path.GetFileName(scriptPath)}");
            int rc = ProcessRunner.Run("powershell.exe", args, log, Path.GetDirectoryName(scriptPath));
            log?.Invoke(rc == 0 ? "✓ powershell OK (exit 0)" : $"✗ powershell exit {rc}");
            return rc;
        }

        // ── Batch ────────────────────────────────────────────
        public int RunBatch(string scriptPath, Action<string> log)
        {
            string args = $"/c {Quote(scriptPath)}";
            log?.Invoke($"[cmd] {Path.GetFileName(scriptPath)}");
            int rc = ProcessRunner.Run("cmd.exe", args, log, Path.GetDirectoryName(scriptPath));
            log?.Invoke(rc == 0 ? "✓ cmd OK (exit 0)" : $"✗ cmd exit {rc}");
            return rc;
        }

        private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";
    }
}
