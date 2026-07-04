using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Logica opzionale "genera log" per i task: crea la cartella log\ accanto al task e
    /// vi scrive l'output di ogni esecuzione. Due strategie a seconda del programma:
    ///  • sqlcmd  → aggiunge  -o "log\&lt;nome&gt;.log"  agli argomenti (semplice, editabile).
    ///  • altri    → l'azione va avvolta in  cmd.exe /c ""prog" args &gt; "log" 2&gt;&amp;1"
    ///               (fatto DOPO la creazione via PowerShell Set-ScheduledTask, perché le
    ///               quote annidate NON sopravvivono al /TR di schtasks — testato).
    /// </summary>
    public static class TaskLogging
    {
        /// <summary>
        /// True se il programma è sqlcmd. NON usa Path.* (esplode con "Illegal characters in path"
        /// se il Program di un task esistente contiene virgolette o altri char non validi).
        /// </summary>
        public static bool IsSqlcmd(string program)
        {
            string p = StripQuotes(program);
            int slash = p.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) p = p.Substring(slash + 1);
            int dot = p.LastIndexOf('.');
            if (dot > 0) p = p.Substring(0, dot);
            return p.Equals("sqlcmd", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Rimuove virgolette e spazi esterni da un percorso programma.</summary>
        public static string StripQuotes(string s) => (s ?? "").Trim().Trim('"').Trim();

        /// <summary>Percorso del file di log: &lt;baseDir&gt;\log\&lt;taskName&gt;.log.</summary>
        public static string LogPathFor(string workingDir, string program, string arguments, string taskName)
        {
            string baseDir = workingDir;
            if (string.IsNullOrWhiteSpace(baseDir)) baseDir = DirFromArgs(arguments);
            if (string.IsNullOrWhiteSpace(baseDir)) baseDir = SafeDir(program);
            if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "log", Sanitize(taskName) + ".log");
        }

        /// <summary>Crea la cartella log\ (best effort). Ritorna false se non riesce.</summary>
        public static bool EnsureLogDir(string logFile)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(logFile)); return true; }
            catch { return false; }
        }

        /// <summary>Aggiunge -o "log" agli argomenti sqlcmd (se non già presente).</summary>
        public static string AppendSqlcmdOutput(string arguments, string logFile)
        {
            arguments = arguments ?? "";
            if (Regex.IsMatch(arguments, @"(^|\s)-o(\s|$)", RegexOptions.IgnoreCase)) return arguments;
            return (arguments.TrimEnd() + " -o \"" + logFile + "\"").Trim();
        }

        /// <summary>
        /// Stringa Argument per l'azione avvolta in cmd:  /c ""prog" args &gt; "log" 2&gt;&amp;1"
        /// (le doppie virgolette esterne fanno sì che cmd /c mantenga l'intera riga).
        /// </summary>
        public static string BuildCmdWrapperArgument(string program, string arguments, string logFile)
        {
            string inner = "\"" + StripQuotes(program) + "\"";
            if (!string.IsNullOrWhiteSpace(arguments)) inner += " " + arguments.Trim();
            return "/c \"" + inner + " > \"" + logFile + "\" 2>&1\"";
        }

        // ── Helpers ──────────────────────────────────────────
        // Cerca nei argomenti il primo path di script (-i / -File / path quotato .sql/.ps1/.bat).
        private static string DirFromArgs(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments)) return null;
            var m = Regex.Match(arguments, "\"([^\"]+\\.(?:sql|ps1|bat|cmd))\"", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(arguments, @"(?:-i|-File)\s+([^\s""]+\.(?:sql|ps1|bat|cmd))", RegexOptions.IgnoreCase);
            if (m.Success) { try { return Path.GetDirectoryName(m.Groups[1].Value); } catch { } }
            return null;
        }

        private static string SafeDir(string program)
        {
            try { string d = Path.GetDirectoryName(program); return string.IsNullOrWhiteSpace(d) ? null : d; }
            catch { return null; }
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace('\\', '_');
        }
    }
}
