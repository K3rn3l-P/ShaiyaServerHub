using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Strumenti Sysinternals esterni (autorunsc.exe) usati dalla Test Suite e dall'AI Hub per l'audit
    /// persistenza/ASEP (config-drift). Binario NON versionato nel repo (vedi .gitignore
    /// "/ShaiyaServerHub/tools/") — scaricato a runtime da live.sysinternals.com, stesso trattamento di
    /// .env/hub.settings.xml (locale al deploy, mai committato). Uso personale confermato dall'utente:
    /// nessuna gestione EULA/redistribuzione oltre al flag -accepteula passato ad ogni invocazione.
    /// </summary>
    public static class ExternalToolsManager
    {
        public static string ToolsDir => Path.Combine(AppContext.BaseDirectory, "tools", "sysinternals");
        public static string AutorunscPath => Path.Combine(ToolsDir, "autorunsc.exe");
        public static string BaselinePath => Path.Combine(ToolsDir, "autoruns-baseline.csv");

        public static bool IsAutorunscPresent => File.Exists(AutorunscPath);

        /// <summary>Stato leggibile per la UI Test Suite (presente+dimensione+data, o mancante).</summary>
        public static string AutorunscStatusText()
        {
            if (!IsAutorunscPresent) return L.ToolsAutorunsMissing;
            var fi = new FileInfo(AutorunscPath);
            return L.ToolsAutorunsPresent(fi.Length / 1024, fi.LastWriteTime);
        }

        /// <summary>
        /// Scarica autorunsc.exe da live.sysinternals.com (file diretto, no zip/installer — niente
        /// dipendenza da winget, non garantito presente su Windows Server). Sovrascrive se già presente
        /// (aggiornamento). Non tocca il baseline (autoruns-baseline.csv): un binario aggiornato non
        /// invalida lo snapshot di autostart già raccolto.
        /// </summary>
        public static async Task<bool> DownloadAutorunscAsync(Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(ToolsDir);
                log?.Invoke("Download autorunsc.exe da live.sysinternals.com...");
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                using (var resp = await http.GetAsync("https://live.sysinternals.com/autorunsc.exe"))
                {
                    resp.EnsureSuccessStatusCode();
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    if (bytes == null || bytes.Length < 10_000)
                        throw new IOException($"file scaricato sospettosamente piccolo ({bytes?.Length ?? 0} byte)");
                    File.WriteAllBytes(AutorunscPath, bytes);
                }
                log?.Invoke($"✓ autorunsc.exe scaricato ({new FileInfo(AutorunscPath).Length / 1024} KB).");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke("✗ ERRORE download: " + ex.Message);
                App.WriteLog("EXTERNAL-TOOLS", "download autorunsc fallito: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Esegue lo script Test Suite 22_autoruns_baseline.ps1 (fresco, non cache) e ne estrae
        /// Status/Detail dall'oggetto {Name,Status,Detail} — STESSO script/baseline usati dal badge
        /// Test Suite e dallo scheduler giornaliero (unica fonte di verità, nessun parsing duplicato).
        /// </summary>
        public static bool RunAutorunsBaselineCheck(out string status, out string detail)
        {
            status = "ERRORE"; detail = "";
            string scriptPath = Path.Combine(ScriptRepository.TestSuite.Ps1Dir, "22_autoruns_baseline.ps1");
            if (!File.Exists(scriptPath)) { detail = "script 22_autoruns_baseline.ps1 non trovato"; return false; }

            string p = scriptPath.Replace("'", "''");
            string args = "-NoProfile -ExecutionPolicy Bypass -Command \"& '" + p +
                          "' | Format-List | Out-String -Width 4000\"";
            var sb = new StringBuilder();
            int rc = ProcessRunner.Run("powershell.exe", args, l => sb.AppendLine(l), Path.GetDirectoryName(scriptPath));

            foreach (var line in sb.ToString().Replace("\r", "").Split('\n'))
            {
                string t = line.TrimStart();
                int c = t.IndexOf(':');
                if (c < 0) continue;
                if (t.StartsWith("Status", StringComparison.OrdinalIgnoreCase)) status = t.Substring(c + 1).Trim();
                else if (t.StartsWith("Detail", StringComparison.OrdinalIgnoreCase)) detail = t.Substring(c + 1).Trim();
            }
            if (rc != 0 && status == "ERRORE" && string.IsNullOrEmpty(detail))
                detail = $"powershell exit {rc}";
            return true;
        }
    }
}
