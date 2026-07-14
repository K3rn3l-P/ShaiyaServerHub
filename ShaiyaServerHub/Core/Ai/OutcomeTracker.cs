using System;
using System.IO;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>Conteggi aggregati per categoria — vedi <see cref="OutcomeTracker.GetStats"/>.</summary>
    public class OutcomeStats
    {
        public int Total, Executed, Approved, Denied, AutoExecuted, TakenOver, Failed;
    }

    /// <summary>
    /// Registro PERSISTITO (sopravvive al riavvio dell'Hub, a differenza di <see cref="ActionQueue"/>
    /// che è solo in-memory) di ogni esito di azione T1+ — vedi PLAN §Componenti codice. Scrive su file
    /// invece che tenerlo solo in memoria proprio perché serve a costruire uno storico nel tempo (base
    /// per un futuro shadow-mode/precisione-per-categoria, non ancora implementato: qui c'è solo la
    /// raccolta dati, il gating automatico su nuove categorie è un affinamento successivo quando ci
    /// saranno abbastanza categorie diverse da giustificarlo). Formato riga semplice, leggibile a mano:
    /// timestampUTC|categoria|esito|dettaglio.
    /// </summary>
    public static class OutcomeTracker
    {
        private static string LogPath => Path.Combine(AiPaths.LearnedDir, "outcomes.log");
        private static readonly object _lock = new object();

        public static void Record(string category, string outcome, string detail)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(AiPaths.LearnedDir);
                    string flatDetail = (detail ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                    if (flatDetail.Length > 300) flatDetail = flatDetail.Substring(0, 300) + "…";
                    string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z|{category}|{outcome}|{flatDetail}";
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch (Exception ex) { App.WriteLog("AI-OUTCOMETRACKER", "errore scrittura: " + ex.Message); }
        }

        /// <summary>Conteggi per categoria (nome tool) — letti dal file ad ogni chiamata, nessuna cache (uso saltuario, non un hot path).</summary>
        public static OutcomeStats GetStats(string category)
        {
            var stats = new OutcomeStats();
            try
            {
                if (!File.Exists(LogPath)) return stats;
                foreach (var line in File.ReadAllLines(LogPath))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    if (!string.Equals(parts[1], category, StringComparison.OrdinalIgnoreCase)) continue;
                    stats.Total++;
                    switch (parts[2].ToLowerInvariant())
                    {
                        case "executed": stats.Executed++; break;
                        case "approved": stats.Approved++; break;
                        case "denied": stats.Denied++; break;
                        case "autoexecuted": stats.AutoExecuted++; break;
                        case "takenover": stats.TakenOver++; break;
                        case "failed": stats.Failed++; break;
                    }
                }
            }
            catch { }
            return stats;
        }
    }
}
