using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Lettura dei log di sistema dei servizi ps_* (`C:\ShaiyaServer\PSM_Client\Bin\Log\
    /// PS_&lt;NOME&gt;__system.log__&lt;timestamp&gt;.log`, nuovo file ad ogni avvio del processo —
    /// formato confermato sul server reale via SSH). Condiviso da <see cref="Ai.Tools.ReadServiceLogTool"/>
    /// (lettura T0 su richiesta) e da <see cref="Ai.Tools.KickPlayerTool"/> (verifica automatica
    /// dell'esito reale di un comando slash — vedi <see cref="TryVerifySlashCommand"/>).
    /// </summary>
    public static class ServiceLogReader
    {
        public static bool TryReadLatestTail(HubSettings settings, string service, int lines,
            out string fileName, out string content, out string error)
        {
            fileName = null; content = null; error = null;
            string logDir = Path.Combine(settings?.ServerBinPath ?? @"C:\ShaiyaServer\PSM_Client\Bin", "Log");
            if (!Directory.Exists(logDir)) { error = $"Cartella log non trovata: {logDir}"; return false; }

            string prefix = (service ?? "").ToUpperInvariant();
            var candidates = Directory.GetFiles(logDir, $"{prefix}__system.log__*.log");
            if (candidates.Length == 0)
            {
                error = $"Nessun log trovato per '{service}' (cercato '{prefix}__system.log__*.log' in {logDir}).";
                return false;
            }

            string latest = candidates.OrderByDescending(File.GetLastWriteTime).First();
            fileName = Path.GetFileName(latest);
            string[] allLines;
            using (var fs = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
                allLines = sr.ReadToEnd().Replace("\r", "").Split('\n');

            content = string.Join("\n", allLines.Skip(Math.Max(0, allLines.Length - lines)));
            return true;
        }

        /// <summary>
        /// Verifica l'esito REALE di un comando slash appena inviato via `usp_RunCommand`, leggendo il
        /// log del servizio invece di fidarsi del solo exit code SQL — quello riflette solo "l'EXEC ha
        /// funzionato come istruzione T-SQL", MAI se il comando ha avuto effetto lato gioco (osservato:
        /// `/kickun` con un nome-personaggio invece dello username restituisce rc=0 lato SQL ma il log
        /// ps_game dice esplicitamente "cmd kick by user name(...) failed"). Cerca l'ultima riga
        /// `&lt;Console input&gt; /comando target` nel log e la riga di output subito dopo.
        /// True/false/null in <paramref name="verifiedOk"/>: true=confermato "ok", false=confermato
        /// "failed", null=nessuna riga di output trovata entro l'attesa (inconclusivo, non un errore).
        /// </summary>
        public static void TryVerifySlashCommand(HubSettings settings, string service, string command, string target,
            out bool? verifiedOk, out string detail)
        {
            verifiedOk = null;
            detail = "Nessuna conferma trovata nel log entro il tempo di attesa.";
            try
            {
                // Il log si popola appena il processo di gioco elabora il comando in coda — nei test
                // osservati l'input e l'output comparivano nello stesso secondo, 1.5s dà margine su un
                // server sotto carico senza allungare troppo il turno.
                Thread.Sleep(1500);

                if (!TryReadLatestTail(settings, service, 80, out _, out string tail, out string err) || tail == null)
                {
                    detail = "Impossibile leggere il log per verificare: " + (err ?? "sconosciuto");
                    return;
                }

                string needle = $"<Console input> /{command} {target}";
                var lines = tail.Split('\n');
                int lastMatch = -1;
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        lastMatch = i;

                if (lastMatch < 0) { detail = $"Riga di comando \"{needle}\" non trovata nel log — nessuna conferma."; return; }
                if (lastMatch + 1 >= lines.Length) { detail = "Comando trovato nel log ma nessuna riga di output dopo."; return; }

                string outputLine = lines[lastMatch + 1].Trim();
                detail = outputLine;
                if (outputLine.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0) verifiedOk = false;
                else if (outputLine.IndexOf(" ok", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         outputLine.EndsWith("ok", StringComparison.OrdinalIgnoreCase) ||
                         outputLine.IndexOf("ok!", StringComparison.OrdinalIgnoreCase) >= 0)
                    verifiedOk = true;
                // altrimenti resta null: riga trovata ma formato non riconosciuto, non spacciarla per ok.
            }
            catch (Exception ex) { detail = "Errore verifica log: " + ex.Message; }
        }
    }
}
