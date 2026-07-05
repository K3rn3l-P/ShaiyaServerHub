using System;
using System.IO;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Riassunto dell'ultimo esito della Test Suite per la card della Dashboard.
    /// Conta gli esiti PER-SCRIPT dal log dell'area (testsuite\testsuite.log) via
    /// <see cref="AreaLogParser"/> — funziona sia dopo "▶ Esegui tutto" sia dopo run
    /// singoli, NON dipende dalla riga di riassunto "TOTALE:".
    /// </summary>
    public class TestSuiteReport
    {
        public bool HasData;
        public int Pass, Warn, Fail, Error, Info, Total;
        public DateTime When;

        /// <summary>Pallino rosso: errori d'esecuzione contano come FAIL.</summary>
        public int FailTotal => Fail + Error;

        private static string LogPath =>
            Path.Combine(ScriptRepository.TestSuite.Root, "testsuite.log");

        public static TestSuiteReport Load()
        {
            var r = new TestSuiteReport();
            try
            {
                string path = LogPath;
                if (!File.Exists(path)) return r;
                r.When = File.GetLastWriteTime(path);

                var map = AreaLogParser.ParsePerScript(path);
                if (map.Count == 0) return r;

                foreach (var o in map.Values)
                {
                    switch (o.Status)
                    {
                        case "PASS": r.Pass++; break;
                        case "WARN": r.Warn++; break;
                        case "FAIL": r.Fail++; break;
                        case "ERRORE": r.Error++; break;
                        default: r.Info++; break;
                    }
                }
                r.Total = map.Count;
                r.HasData = true;
            }
            catch { }
            return r;
        }
    }
}
