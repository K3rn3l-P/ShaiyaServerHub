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

        /// <summary>
        /// True se l'azione è l'exe del Hub in modalità headless (--run "&lt;script&gt;").
        /// In tal caso il log NON va fatto col redirect cmd (l'exe è WinExe, senza stdout):
        /// si passa --log "&lt;file&gt;" e ci pensa RunHeadless a scriverci.
        /// </summary>
        public static bool IsHubRun(string program, string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments) ||
                !Regex.IsMatch(arguments, @"(^|\s)--run(\s|$)", RegexOptions.IgnoreCase))
                return false;
            return IsHubExe(program);
        }

        /// <summary>
        /// True se il programma è l'eseguibile del Hub stesso (ShaiyaServerHub.exe), a PRESCINDERE
        /// dagli argomenti. Serve alla guardia: l'exe del Hub senza --run partirebbe in modalità UI
        /// (WinExe headless) → si appende per sempre e, se avvolto in "cmd > log", locka il file di log.
        /// </summary>
        public static bool IsHubExe(string program)
        {
            string p = StripQuotes(program);
            int slash = p.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) p = p.Substring(slash + 1);
            return p.StartsWith("ShaiyaServerHub", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Aggiunge --log "&lt;file&gt;" agli argomenti --run (se non già presente).</summary>
        public static string AppendHubLog(string arguments, string logFile)
        {
            arguments = arguments ?? "";
            if (Regex.IsMatch(arguments, @"(^|\s)--log(\s|$)", RegexOptions.IgnoreCase)) return arguments;
            return (arguments.TrimEnd() + " --log \"" + logFile + "\"").Trim();
        }

        /// <summary>
        /// Auth per-task: rimuove "--auth &lt;mode&gt;" dagli argomenti e ritorna la modalità normalizzata
        /// ("Windows" | "Sql" | "Default"). --auth NON è un segreto (solo win/sql), quindi vive negli args.
        /// </summary>
        public static string ExtractAuthMode(ref string arguments)
        {
            arguments = arguments ?? "";
            var m = Regex.Match(arguments, @"\s*--auth\s+(\w+)", RegexOptions.IgnoreCase);
            if (!m.Success) return "Default";
            string mode = m.Groups[1].Value.ToLowerInvariant();
            arguments = (arguments.Substring(0, m.Index) + arguments.Substring(m.Index + m.Length)).Trim();
            return mode == "win" || mode == "windows" ? "Windows" : (mode == "sql" ? "Sql" : "Default");
        }

        /// <summary>Token "--auth win|sql" per una modalità (stringa vuota se Default).</summary>
        public static string AuthArg(string mode)
        {
            if ("Windows".Equals(mode, StringComparison.OrdinalIgnoreCase)) return "--auth win";
            if ("Sql".Equals(mode, StringComparison.OrdinalIgnoreCase)) return "--auth sql";
            return "";
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

        /// <summary>
        /// Auto-rilevamento (per l'Edit): se l'azione ha il logging attivo, la riporta alla forma
        /// "pulita" (program+arguments senza log) e ritorna true. Gestisce le tre forme prodotte
        /// da "Genera log": wrapper cmd (redirect &gt;), sqlcmd (-o "file"), hub --run (--log "file").
        /// Così l'Edit mostra i valori originali e ri-spunta correttamente la checkbox.
        /// </summary>
        public static bool TryStripLogging(ref string program, ref string arguments)
        {
            bool gen = false;
            arguments = arguments ?? "";

            // 1) Wrapper cmd:  cmd.exe /c "\"prog\" args > \"log\" 2>&1"  → sprogramma inner.
            if (BaseName(program).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                BaseName(program).Equals("cmd", StringComparison.OrdinalIgnoreCase))
            {
                string inner = UnwrapCmd(arguments);
                if (inner != null)
                {
                    SplitProgramArgs(inner, out string ip, out string ia);
                    if (!string.IsNullOrWhiteSpace(ip)) { program = ip; arguments = ia; gen = true; }
                }
            }

            // 2) Hub --run con --log "file"  → rimuovi --log <token>.
            var reLog = new Regex(@"\s*--log\s+(""[^""]*""|\S+)", RegexOptions.IgnoreCase);
            if (reLog.IsMatch(arguments)) { arguments = reLog.Replace(arguments, "").Trim(); gen = true; }

            // 3) sqlcmd con -o "file"  → rimuovi -o <token>.
            if (IsSqlcmd(program))
            {
                var reO = new Regex(@"\s*-o\s+(""[^""]*""|\S+)", RegexOptions.IgnoreCase);
                if (reO.IsMatch(arguments)) { arguments = reO.Replace(arguments, "").Trim(); gen = true; }
            }

            return gen;
        }

        // ── Helpers ──────────────────────────────────────────
        // Nome file del programma (senza dir, senza virgolette).
        private static string BaseName(string program)
        {
            string p = StripQuotes(program);
            int slash = p.LastIndexOfAny(new[] { '\\', '/' });
            return slash >= 0 ? p.Substring(slash + 1) : p;
        }

        // "/c \"...\" > \"log\" 2>&1"  →  "\"prog\" args"  (inner senza /c, senza wrapper, senza redirect).
        // Ritorna null se la stringa non è un wrapper cmd riconoscibile.
        private static string UnwrapCmd(string arguments)
        {
            string s = (arguments ?? "").Trim();
            var mC = Regex.Match(s, @"^/c\s+", RegexOptions.IgnoreCase);
            if (!mC.Success) return null;
            s = s.Substring(mC.Length).Trim();
            // Togli l'eventuale coppia di virgolette esterne del wrapper.
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            // Togli il redirect finale:  > "log" 2>&1   (o  > log 2>&1).
            string stripped = Regex.Replace(s, @"\s*>\s*(""[^""]*""|\S+)\s*2>&1\s*$", "", RegexOptions.IgnoreCase);
            if (stripped == s) return null;   // niente redirect → non era il nostro wrapper log
            return stripped.Trim();
        }

        // "\"prog\" args"  →  prog (senza virgolette) + args. Gestisce anche prog non quotato.
        private static void SplitProgramArgs(string s, out string prog, out string args)
        {
            s = (s ?? "").Trim(); prog = ""; args = "";
            if (s.Length == 0) return;
            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 0) { prog = s.Substring(1, end - 1); args = s.Substring(end + 1).Trim(); return; }
            }
            int sp = s.IndexOf(' ');
            if (sp < 0) { prog = s; args = ""; }
            else { prog = s.Substring(0, sp); args = s.Substring(sp + 1).Trim(); }
        }

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
