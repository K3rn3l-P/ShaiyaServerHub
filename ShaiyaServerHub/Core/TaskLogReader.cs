using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core
{
    /// <summary>Esito dell'ultima esecuzione ricavato dal log del task.</summary>
    public enum TaskLogState { None, Ok, Error }

    /// <summary>
    /// Lettura dei log per-task ("Genera log" → &lt;baseDir&gt;\log\&lt;nome&gt;.log).
    /// baseDir a runtime = cartella dell'exe (dove gira il Hub sul server), coerente con
    /// TaskLogging.LogPathFor per i task hub (--run) e col --log passato all'exe.
    /// </summary>
    public static class TaskLogReader
    {
        public static string LogPathFor(string taskName) =>
            Path.Combine(AppContext.BaseDirectory, "log", Sanitize(taskName) + ".log");

        public static bool Exists(string taskName) => File.Exists(LogPathFor(taskName));

        /// <summary>Ultima modifica del log (DateTime.MinValue se non esiste). Per l'auto-refresh dopo "Ora".</summary>
        public static DateTime LastWrite(string taskName)
        {
            try { string p = LogPathFor(taskName); return File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        /// <summary>Contenuto del log (coda) per la visualizzazione. Null se il file non esiste.</summary>
        public static string Read(string taskName, out string path)
        {
            path = LogPathFor(taskName);
            try { return File.Exists(path) ? ReadTail(path, 200_000) : null; }
            catch (Exception ex) { return "[impossibile leggere il log: " + ex.Message + "]"; }
        }

        /// <summary>
        /// Esito dell'ULTIMO run: analizza solo l'ultimo blocco (dopo l'ultima riga "── headless:").
        /// Priorità: marker "ERRORE:" &gt; exit code &gt; parole chiave d'errore &gt; ok.
        /// </summary>
        public static TaskLogState LastRunState(string taskName)
        {
            try
            {
                string path = LogPathFor(taskName);
                if (!File.Exists(path)) return TaskLogState.None;
                string text = ReadTail(path, 32_000);
                if (string.IsNullOrWhiteSpace(text)) return TaskLogState.None;

                string[] lines = text.Replace("\r", "").Split('\n');
                int start = 0;
                for (int i = lines.Length - 1; i >= 0; i--)
                    if (lines[i].Contains("── headless:")) { start = i; break; }

                bool sawExit = false, exitErr = false, hardErr = false, kwErr = false;
                for (int i = start; i < lines.Length; i++)
                {
                    string ln = lines[i];
                    var m = Regex.Match(ln, @"\bexit\s+(-?\d+)", RegexOptions.IgnoreCase);
                    if (m.Success) { sawExit = true; if (int.Parse(m.Groups[1].Value) != 0) exitErr = true; }
                    if (ln.IndexOf("ERRORE:", StringComparison.OrdinalIgnoreCase) >= 0) hardErr = true;
                    else if (Regex.IsMatch(ln, @"\b(error|exception|failed|fatal)\b|Msg\s+\d+", RegexOptions.IgnoreCase)) kwErr = true;
                }

                if (hardErr) return TaskLogState.Error;
                if (sawExit) return exitErr ? TaskLogState.Error : TaskLogState.Ok;   // exit code = fonte primaria
                if (kwErr) return TaskLogState.Error;                                 // euristica solo senza exit
                return TaskLogState.Ok;
            }
            catch { return TaskLogState.None; }
        }

        // Legge solo la coda del file (ultimi maxBytes) — evita di caricare log enormi. Share ReadWrite
        // così si può leggere anche mentre un task ci sta scrivendo.
        private static string ReadTail(string path, int maxBytes)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long start = fs.Length > maxBytes ? fs.Length - maxBytes : 0;
                fs.Seek(start, SeekOrigin.Begin);
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace('\\', '_');
        }
    }
}
