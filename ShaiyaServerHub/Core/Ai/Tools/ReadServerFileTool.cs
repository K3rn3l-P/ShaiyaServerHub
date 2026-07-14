using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: lettura sola-lettura dell'intero albero server (C:\ShaiyaServer — PSM_Client\Bin\Data\
    /// con GMSecurity.ini/EtainShield.ini, Logs\, Scripts\, ecc.), SEPARATO da <see cref="ReadHubFileTool"/>
    /// (quello è solo la cartella del Hub) e <see cref="ReadWebServerFileTool"/> (quello è solo il sito
    /// sotto XamppRoot). Colma un gap reale: GMSecurity.ini/EtainShield.ini sono file di configurazione
    /// CRITICI per capire le difese del server (IP_WHITELIST admin, soglie anti-cheat/anti-tamper) ma
    /// prima non erano leggibili da nessun tool.
    ///
    /// GMSecurity.ini contiene anche VERI SEGRETI in chiaro (TokenSecret/ClientSecret/SahKeyHex/
    /// PatchKeyHex/HwidSigKey — le chiavi crittografiche di tutto il protocollo client-server, vedi
    /// ..\CLAUDE.md "NESSUN SEGRETO NEL CLIENT"): redatti automaticamente da <see cref="SecretRedactor"/>
    /// (rinforzato apposta dopo aver verificato che la versione precedente del pattern NON catturava
    /// "SahKeyHex="/"HwidSigKey=" — richiedeva "api_key"/"private_key" col prefisso, non "key" da solo).
    /// Le sezioni [IP_WHITELIST]/[ANTI_TAMPER] restano visibili (nessuna parola sensibile nei nomi
    /// campo), che è esattamente il contenuto utile per l'AI. Path SEMPRE ristretto sotto la root
    /// server (traversal bloccato), nessuna scrittura possibile, solo estensioni di testo/config/log
    /// (niente .exe/.dll/.ps1/.sql — area non ancora censita a fondo, scope volutamente più stretto di
    /// ReadHubFileTool).
    /// </summary>
    public class ReadServerFileTool : IAiTool
    {
        private readonly HubSettings _settings;
        private const long MaxFileBytes = 300_000;

        private static readonly string[] AllowedExt =
        {
            ".ini", ".conf", ".cnf", ".log", ".txt", ".md", ".json", ".xml", ".yaml", ".yml"
        };

        public string Name => "read_server_file";
        public string Description => "Legge file o elenca cartelle dell'intero albero del server di gioco " +
            "(C:\\ShaiyaServer — es. 'PSM_Client/Bin/Data/GMSecurity.ini' per IP_WHITELIST admin + soglie " +
            "ANTI_TAMPER, 'PSM_Client/Bin/Data/EtainShield.ini' per soglie anti-speedhack/rangehack/cutting, " +
            "'Logs/...' per log di sistema). Path relativo alla root del server, o '' per elencare la radice. " +
            "Sola lettura, righe che sembrano contenere segreti (chiavi crittografiche, non solo password) " +
            "vengono redatte automaticamente — GMSecurity.ini in particolare contiene VERE chiavi in chiaro " +
            "(TokenSecret/ClientSecret/SahKeyHex/ecc.), mai mostrarle anche se richiesto esplicitamente: la " +
            "redazione è automatica e non aggirabile.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"path\":{\"type\":\"string\",\"description\":\"Path relativo alla root del server, es. 'PSM_Client/Bin/Data/GMSecurity.ini' o '' per la radice\"}}," +
            "\"required\":[\"path\"]}";

        public ReadServerFileTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string relPath = "";
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                    if (doc.RootElement.TryGetProperty("path", out var p)) relPath = p.GetString() ?? "";

                string root = ServerRoot();
                string rootFull = Path.GetFullPath(root);

                string target;
                try { target = Path.GetFullPath(Path.Combine(rootFull, relPath.TrimStart('/', '\\'))); }
                catch (Exception ex) { return "ERRORE: path non valido: " + ex.Message; }

                if (!IsInside(rootFull, target))
                    return $"RIFIUTATO: path fuori dalla root del server ({root}).";

                if (Directory.Exists(target))
                {
                    var entries = Directory.GetFileSystemEntries(target)
                        .Select(e => (Directory.Exists(e) ? "[dir]  " : "       ") + Path.GetFileName(e))
                        .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return entries.Count == 0 ? "(cartella vuota)" : string.Join("\n", entries);
                }

                if (!File.Exists(target)) return $"ERRORE: '{relPath}' non trovato sotto la root del server.";

                string ext = Path.GetExtension(target);
                if (!AllowedExt.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    return $"RIFIUTATO: estensione '{ext}' non consentita (solo config/log/documentazione di testo).";

                var fi = new FileInfo(target);
                if (fi.Length > MaxFileBytes)
                    return $"ERRORE: file troppo grande ({fi.Length / 1024} KB, limite {MaxFileBytes / 1024} KB).";

                string content = File.ReadAllText(target, System.Text.Encoding.UTF8);
                return SecretRedactor.Redact(content);
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }

        // C:\ShaiyaServer\PSM_Client\Bin -> risale di 2 livelli -> C:\ShaiyaServer. Fallback hardcoded
        // se ServerBinPath non è configurato o ha una forma inattesa (mai far fallire silenziosamente
        // su una root sbagliata: meglio il default noto che una sorpresa).
        private string ServerRoot()
        {
            try
            {
                string bin = _settings?.ServerBinPath;
                if (!string.IsNullOrWhiteSpace(bin))
                {
                    var parent = Directory.GetParent(bin)?.Parent;
                    if (parent != null) return parent.FullName;
                }
            }
            catch { }
            return @"C:\ShaiyaServer";
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
