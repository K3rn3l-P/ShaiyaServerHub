using System;
using System.IO;

namespace ShaiyaServerHub.Core
{
    /// <summary>Esegue uno script (SQL via sqlcmd, PS1 via powershell, BAT via cmd).</summary>
    public class ScriptRunner
    {
        private readonly HubSettings _settings;
        public ScriptRunner(HubSettings settings) { _settings = settings; }

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
        // -W rimuove gli spazi di padding dalle colonne, -w allarga la riga, -s separatore.
        // Evita l'output "spaziato" di sqlcmd sui SELECT con colonne nvarchar larghe.
        private const string FmtArgs = " -W -w 4000 -s \"|\"";

        public int RunSql(string scriptPath, Action<string> log)
        {
            string args = BuildSqlcmdAuth() + FmtArgs +
                          $" -d {Quote(_settings.DefaultDatabase)} -b -i {Quote(scriptPath)}";
            log?.Invoke($"[sqlcmd] {Path.GetFileName(scriptPath)}  (server={_settings.SqlServer}, auth={_settings.SqlAuthMode})");
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
            if (string.Equals(_settings.SqlAuthMode, "Sql", StringComparison.OrdinalIgnoreCase))
                return baseArgs + $" -U {Quote(_settings.SqlUser)} -P {Quote(_settings.SqlPassword)}";
            return baseArgs + " -E"; // Windows auth
        }

        // ── PowerShell ───────────────────────────────────────
        public int RunPowerShell(string scriptPath, Action<string> log)
        {
            string args = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)}";
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
