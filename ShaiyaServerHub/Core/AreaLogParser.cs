using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core
{
    /// <summary>Esito persistito di un singolo script nel log di un'area.</summary>
    public class ScriptRunOutcome
    {
        public string Status;   // PASS | WARN | FAIL | ERRORE | INFO
        public string Log;      // blocco di log del suo run più recente
    }

    /// <summary>
    /// Parsa il log di un'area (es. testsuite\testsuite.log, functions\functions.log) e ricava,
    /// per ciascuno script, l'ESITO e il BLOCCO DI LOG del suo run PIÙ RECENTE. Stessa logica dei
    /// badge live di ScriptAreaControl.RunOne, ma letta dal file → sopravvive a reload/riavvio.
    ///
    /// Marker inizio blocco (ASCII, sempre emessi da ScriptRunner):
    ///   [sqlcmd] name (server=…) · [powershell] name · [cmd] name
    /// Esito: exit!=0 ⇒ ERRORE; altrimenti l'ultima riga RESULT:/Status: del blocco; nessuno ⇒ INFO.
    /// </summary>
    public static class AreaLogParser
    {
        private static readonly Regex RxPrefix = new Regex(@"^\[[0-9\-: ]+\]\s*", RegexOptions.Compiled);
        private static readonly Regex RxStart  = new Regex(@"^\[(?:sqlcmd|powershell|cmd)\]\s+(\S+)", RegexOptions.Compiled);
        private static readonly Regex RxExit    = new Regex(@"\bexit\s+(-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Esito per-script (chiave = nome file), ultimo run vince.</summary>
        public static Dictionary<string, ScriptRunOutcome> ParsePerScript(string logPath, int maxBytes = 400_000)
        {
            var map = new Dictionary<string, ScriptRunOutcome>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return map;
                string text = ReadTail(logPath, maxBytes);
                if (string.IsNullOrWhiteSpace(text)) return map;

                string cur = null, status = null;
                var block = new StringBuilder();

                Action flush = () =>
                {
                    if (cur == null) return;
                    map[cur] = new ScriptRunOutcome   // ultimo run dello script vince
                    {
                        Status = status ?? "INFO",
                        Log = block.ToString().TrimEnd()
                    };
                };

                foreach (var raw in text.Replace("\r", "").Split('\n'))
                {
                    string ln = RxPrefix.Replace(raw, "");   // togli "[yyyy-MM-dd HH:mm:ss] "

                    var mStart = RxStart.Match(ln);
                    if (mStart.Success)
                    {
                        flush();                       // chiudi il blocco precedente
                        cur = mStart.Groups[1].Value;
                        status = null;
                        block = new StringBuilder();
                        block.AppendLine(ln);
                        continue;
                    }
                    if (cur == null) continue;

                    block.AppendLine(ln);

                    var mExit = RxExit.Match(ln);
                    if (mExit.Success)
                    {
                        if (int.Parse(mExit.Groups[1].Value) != 0) status = "ERRORE";   // prevale (come rc)
                        continue;
                    }
                    string s = BadgeFrom(ln);
                    if (s != null) status = s;         // ultimo RESULT/Status del blocco vince
                }
                flush();   // ultimo blocco aperto
            }
            catch { }
            return map;
        }

        // Esito da "RESULT: PASS" (SQL) o "Status : PASS" (check PowerShell via Format-List).
        private static string BadgeFrom(string line)
        {
            string rest = null;
            int idx = line.IndexOf("RESULT:", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                rest = line.Substring(idx + 7);
            }
            else
            {
                string t = line.TrimStart();
                if (t.StartsWith("Status", StringComparison.OrdinalIgnoreCase))
                {
                    int c = t.IndexOf(':');
                    if (c >= 0) rest = t.Substring(c + 1);
                }
            }
            if (rest == null) return null;

            rest = rest.TrimStart();
            if (rest.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) return "FAIL";
            if (rest.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)) return "WARN";
            if (rest.StartsWith("PASS", StringComparison.OrdinalIgnoreCase)) return "PASS";
            if (rest.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) return "ERRORE";
            return null;
        }

        // Coda del file (ultimi maxBytes), Share ReadWrite: leggibile anche durante un run.
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
    }
}
