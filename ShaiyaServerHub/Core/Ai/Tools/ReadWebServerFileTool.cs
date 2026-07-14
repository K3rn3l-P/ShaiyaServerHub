using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: lettura sola-lettura dell'albero del web server (Apache/PHP/sito — tutto sotto
    /// HubSettings.XamppRoot, es. C:\xampp: httpd.conf, php.ini, vhosts, e i sorgenti PHP in htdocs\).
    /// Permette all'AI di valutare la configurazione reale e i sorgenti del sito (non solo l'esito
    /// aggregato della Test Suite) per proporre fix/nuovi controlli mirati.
    ///
    /// Il sito usa credenziali SOLO da variabili d'ambiente (mai hardcoded nei file — vedi HintWebSql/
    /// account Ernoweb@), ma il tool redige comunque righe che sembrano contenere segreti come rete di
    /// sicurezza extra (difesa in profondità, costa nulla). Path SEMPRE ristretto sotto XamppRoot —
    /// nessun modo di uscire dalla cartella (traversal bloccato), nessuna scrittura possibile.
    /// </summary>
    public class ReadWebServerFileTool : IAiTool
    {
        private readonly HubSettings _settings;
        private const long MaxFileBytes = 300_000;

        // Estensioni testuali plausibili in un albero Apache/PHP/XAMPP. File binari/eseguibili esclusi.
        private static readonly string[] AllowedExt =
        {
            ".php", ".ini", ".conf", ".htaccess", ".txt", ".log", ".xml", ".json",
            ".yaml", ".yml", ".cnf", ".env", ".sql", ".md", ".htpasswd"
        };

        public string Name => "read_web_server_file";
        public string Description => "Legge file o elenca cartelle dell'albero del web server (Apache/PHP/" +
            "sito, tutto sotto la XamppRoot configurata — es. httpd.conf, php.ini, vhosts, sorgenti PHP in " +
            "htdocs\\). Path relativo alla XamppRoot (es. 'htdocs/db.php', 'apache/conf/httpd.conf', o '' " +
            "per la radice). Sola lettura, righe che sembrano contenere segreti vengono redatte. Usa questo " +
            "per valutare la configurazione reale o i sorgenti del sito, non solo l'esito della Test Suite.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"path\":{\"type\":\"string\",\"description\":\"Path relativo alla XamppRoot, es. 'htdocs/db.php' o '' per elencare la radice\"}}," +
            "\"required\":[\"path\"]}";

        public ReadWebServerFileTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string relPath = "";
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                    if (doc.RootElement.TryGetProperty("path", out var p)) relPath = p.GetString() ?? "";

                string root = _settings?.XamppRoot;
                if (string.IsNullOrWhiteSpace(root)) return "ERRORE: XamppRoot non configurata nelle Impostazioni.";
                string rootFull = Path.GetFullPath(root);

                string target;
                try { target = Path.GetFullPath(Path.Combine(rootFull, relPath.TrimStart('/', '\\'))); }
                catch (Exception ex) { return "ERRORE: path non valido: " + ex.Message; }

                // Guardia anti-traversal: il target deve stare DENTRO rootFull (confronto su prefisso di cartella,
                // non solo StartsWith, per evitare che "C:\xamppEvil" passi il controllo su "C:\xampp").
                if (!IsInside(rootFull, target))
                    return $"RIFIUTATO: path fuori da XamppRoot ({root}).";

                if (Directory.Exists(target))
                {
                    var entries = Directory.GetFileSystemEntries(target)
                        .Select(e => (Directory.Exists(e) ? "[dir]  " : "       ") + Path.GetFileName(e))
                        .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return entries.Count == 0 ? "(cartella vuota)" : string.Join("\n", entries);
                }

                if (!File.Exists(target)) return $"ERRORE: '{relPath}' non trovato sotto XamppRoot.{SuggestSimilar(target)}";

                string ext = Path.GetExtension(target);
                bool allowed = AllowedExt.Contains(ext, StringComparer.OrdinalIgnoreCase) ||
                               string.Equals(Path.GetFileName(target), ".htaccess", StringComparison.OrdinalIgnoreCase);
                if (!allowed) return $"RIFIUTATO: estensione '{ext}' non consentita (solo file di testo/config).";

                var fi = new FileInfo(target);
                if (fi.Length > MaxFileBytes)
                    return $"ERRORE: file troppo grande ({fi.Length / 1024} KB, limite {MaxFileBytes / 1024} KB).";

                string content = File.ReadAllText(target, System.Text.Encoding.UTF8);
                return SecretRedactor.Redact(content);
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }

        // Stesso fix di ReadHubFileTool.SuggestSimilar — mai indovinare due volte l'estensione giusta.
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
