using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: lettura sola-lettura della cartella del Hub stesso (AppContext.BaseDirectory —
    /// scripts\, log\, ai\, testsuite\, ecc.), SEPARATO da <see cref="ReadWebServerFileTool"/> (quello
    /// legge il sito sotto XamppRoot, una cartella completamente diversa). Colma un gap reale osservato
    /// nel test dal vivo: diagnosticando un task fallito per estensione file sbagliata
    /// (Notice-Maintenance-AUTO.sq invece di .sql), l'AI non aveva modo di leggere il file vero, solo
    /// il log — poteva solo indovinare cosa contenesse realmente la cartella scripts\.
    ///
    /// Blocklist ASSOLUTA (mai leggibili, indipendente da estensione): <c>hub.settings.xml</c> (segreti
    /// DPAPI di tutte le credenziali del Hub) e <c>.env</c> (credenziali IN CHIARO usate dal sito,
    /// iniettate da Build-And-Copy.ps1) — questi file non vengono nemmeno aperti, a differenza della
    /// redazione riga-per-riga usata per gli altri file di testo (difesa in profondità più stretta,
    /// qui il file INTERO è solo credenziali, non ha senso mostrarne parti). Path SEMPRE ristretto
    /// sotto la root del Hub (traversal bloccato), nessuna scrittura possibile.
    /// </summary>
    public class ReadHubFileTool : IAiTool
    {
        private const long MaxFileBytes = 300_000;

        // Estensioni testuali plausibili nella cartella del Hub: script (.sql/.ps1/.bat), log, config,
        // documentazione. Niente .exe/.dll (non in lista = rifiutato per costruzione).
        private static readonly string[] AllowedExt =
        {
            ".sql", ".ps1", ".bat", ".cmd", ".log", ".txt", ".md", ".json",
            ".xml", ".ini", ".cnf", ".yaml", ".yml"
        };

        // Mai leggibili, qualunque estensione — file INTERAMENTE credenziali, non ha senso redigere
        // riga per riga come per un file di config misto.
        private static readonly string[] NeverReadFiles = { "hub.settings.xml", ".env" };

        public string Name => "read_hub_file";
        public string Description => "Legge file o elenca cartelle DENTRO la cartella del Hub stesso (scripts\\sql\\, " +
            "scripts\\ps1\\, log\\, ai\\, testsuite\\, ecc. — NON il sito web, per quello c'è read_web_server_file). " +
            "Path relativo alla root del Hub (es. 'scripts/sql/MioScript.sql', 'log/NomeTask.log', o '' per la " +
            "radice). Sola lettura, righe che sembrano contenere segreti vengono redatte. hub.settings.xml e .env " +
            "sono SEMPRE bloccati (contengono credenziali) indipendentemente dalla richiesta. Usa questo per " +
            "verificare il contenuto REALE di uno script prima di diagnosticare un errore di task pianificato " +
            "(es. estensione sbagliata, script mancante, contenuto inatteso).";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"path\":{\"type\":\"string\",\"description\":\"Path relativo alla root del Hub, es. 'scripts/sql/Foo.sql' o '' per elencare la radice\"}}," +
            "\"required\":[\"path\"]}";

        public string Execute(string argumentsJson)
        {
            try
            {
                string relPath = "";
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                    if (doc.RootElement.TryGetProperty("path", out var p)) relPath = p.GetString() ?? "";

                string rootFull = Path.GetFullPath(AppContext.BaseDirectory);

                string target;
                try { target = Path.GetFullPath(Path.Combine(rootFull, relPath.TrimStart('/', '\\'))); }
                catch (Exception ex) { return "ERRORE: path non valido: " + ex.Message; }

                // Guardia anti-traversal: il target deve stare DENTRO rootFull (confronto su prefisso di
                // cartella, non solo StartsWith, per evitare che "C:\ShaiyaServerHubEvil" passi il controllo).
                if (!IsInside(rootFull, target))
                    return "RIFIUTATO: path fuori dalla cartella del Hub.";

                if (Directory.Exists(target))
                {
                    var entries = Directory.GetFileSystemEntries(target)
                        .Select(e => (Directory.Exists(e) ? "[dir]  " : "       ") + Path.GetFileName(e))
                        .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return entries.Count == 0 ? "(cartella vuota)" : string.Join("\n", entries);
                }

                if (!File.Exists(target)) return $"ERRORE: '{relPath}' non trovato sotto la cartella del Hub.{SuggestSimilar(target)}";

                string fileName = Path.GetFileName(target);
                if (NeverReadFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    return $"RIFIUTATO: '{fileName}' contiene credenziali — mai leggibile da un tool AI, nessuna eccezione.";

                string ext = Path.GetExtension(target);
                if (!AllowedExt.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    return $"RIFIUTATO: estensione '{ext}' non consentita (solo script/log/config di testo).";

                var fi = new FileInfo(target);
                if (fi.Length > MaxFileBytes)
                    return $"ERRORE: file troppo grande ({fi.Length / 1024} KB, limite {MaxFileBytes / 1024} KB).";

                string content = File.ReadAllText(target, System.Text.Encoding.UTF8);
                return SecretRedactor.Redact(content);
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }

        // Osservato nel test dal vivo: il modello tenta il path con l'estensione riportata nel LOG
        // (es. "Notice-Maintenance-AUTO.sq"), che può essere proprio la causa del problema (task
        // configurato con estensione sbagliata) — il file vero esiste con un'altra estensione. Suggerire
        // subito i candidati con lo stesso nome base evita un secondo giro (o, con un modello debole, una
        // risposta finale confusa dopo un fallimento inatteso).
        private static string SuggestSimilar(string target)
        {
            try
            {
                string dir = Path.GetDirectoryName(target);
                string baseName = Path.GetFileNameWithoutExtension(target);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || string.IsNullOrEmpty(baseName)) return "";
                var candidates = Directory.GetFiles(dir)
                    .Select(Path.GetFileName)
                    .Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return candidates.Count > 0
                    ? $" Trovati file con lo STESSO NOME ma estensione diversa nella stessa cartella: {string.Join(", ", candidates)} — probabile match."
                    : "";
            }
            catch { return ""; }
        }

        private static bool IsInside(string root, string target)
        {
            string r = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string t = target.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return t.StartsWith(r, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(target.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
