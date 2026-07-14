using System.Text;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Redazione riga-per-riga condivisa da tutti i tool di lettura file (read_hub_file,
    /// read_web_server_file, read_server_file) — un solo posto da correggere se il pattern si rivela
    /// insufficiente, invece di tre copie che divergono nel tempo. Bug reale trovato: la versione
    /// precedente (una per tool) richiedeva "api_key"/"private_key" col prefisso, quindi NON catturava
    /// chiavi come "TokenSecret="/"SahKeyHex="/"HwidSigKey=" (viste per davvero in GMSecurity.ini) — solo
    /// "secret" e "token" combinati facevano scattare "TokenSecret", ma "SahKeyHex"/"HwidSigKey" con
    /// "key" NON prefissato passavano indenni. Ora basta che il NOME del campo (prima di '='/':')
    /// contenga una delle sottostringhe sensibili, qualunque sia il prefisso/suffisso.
    /// </summary>
    public static class SecretRedactor
    {
        private static readonly Regex SecretLine = new Regex(
            @"^[^=:\r\n]*(password|pwd|secret|key|token|hash|credential)[^=:\r\n]*[:=]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Redige le righe che sembrano contenere segreti; ritorna il testo con nota in testa se ha redatto qualcosa.</summary>
        public static string Redact(string content)
        {
            var sb = new StringBuilder();
            bool redacted = false;
            foreach (var line in (content ?? "").Replace("\r", "").Split('\n'))
            {
                if (SecretLine.IsMatch(line))
                {
                    int idx = line.IndexOfAny(new[] { ':', '=' });
                    sb.AppendLine(idx > 0 ? line.Substring(0, idx + 1) + " [REDATTO]" : "[REDATTO]");
                    redacted = true;
                }
                else sb.AppendLine(line);
            }
            if (redacted) sb.Insert(0, "(alcune righe redatte automaticamente — pattern password/secret/key/token/hash)\n");
            return sb.ToString();
        }
    }
}
