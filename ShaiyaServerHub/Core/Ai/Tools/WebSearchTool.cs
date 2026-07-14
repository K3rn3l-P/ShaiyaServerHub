using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: ricerca web live per mitigazioni/CVE/best-practice, in due modalità configurabili
    /// nelle Impostazioni (scheda AI):
    /// - "Mcp"  (default): server MCP SearXNG (JSON-RPC su HTTP) — lo STESSO canale usato in questa
    ///   sessione Claude Code, verificato manualmente. Vedi <see cref="McpSearxngClient"/>.
    /// - "Rest": endpoint SearXNG REST classico (/search?format=json), per chi in futuro ha/vuole una
    ///   propria istanza SearXNG con l'API JSON abilitata (search: formats: [html, json] in settings.yml
    ///   — SearXNG la disabilita per default e ritorna 403 finché non la si abilita esplicitamente).
    ///
    /// GUARDRAIL NEL CODICE, NON SOLO NEL PROMPT: "openrouter/free" sceglie un modello A CASO ad ogni
    /// chiamata (spesso piccolo/debole) — non si può contare sul fatto che segua un'istruzione
    /// condizionale sulla formulazione della query (osservato: query letterali "Shaiya security" che
    /// pescano solo Facebook/TikTok, nonostante l'istruzione in system.md dicesse di evitarlo). Questo
    /// tool quindi si AUTO-CORREGGE in modo deterministico: se il primo tentativo ritorna SOLO risultati
    /// social/marketing (vedi <see cref="SearchResultFilter"/>) e la query contiene la parola "shaiya"
    /// (termine di nicchia gaming che da solo pesca marketing), ritenta UNA volta rimuovendola — senza
    /// dipendere da nessuna decisione del modello chiamante.
    /// </summary>
    public class WebSearchTool : IAiTool
    {
        private readonly HubSettings _settings;
        private static readonly HttpClient Http = BuildClient();
        private static readonly Regex ShaiyaWord = new Regex(@"\bshaiya\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Un site: troppo mirato su un nodo SearXNG con pochi motori attivi (gli altri sospesi) spesso
        // non trova nulla — non perché l'argomento non esista, ma perché il ristretto sottoinsieme di
        // motori ancora attivi non ha indicizzato QUELLA pagina specifica. Una query più larga (senza
        // site:) ha più chance di intercettare qualcosa dai motori ancora vivi. Stesso principio del
        // retry "shaiya" sotto: deterministico, non lasciato a un modello free che potrebbe non riprovare.
        private static readonly Regex SiteOperator = new Regex(@"\bsite:\S+\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Motori forzati nel retry "zero risultati": gli stessi abilitati nel fix infra CasaOS perché
        // più tolleranti su IP condiviso (vedi memoria searxng-casaos-infra) — verificato empiricamente
        // (ricerca diretta) che interrogarli ESPLICITAMENTE, invece di lasciare il set di default che
        // include motori sospesi CAPTCHA/rate-limit, produce risultati reali dove il default dava zero.
        // Se in futuro cambia quali motori sono abilitati su CasaOS, aggiornare questa lista.
        private const string FallbackEngines = "startpage,bing,mojeek";

        private static HttpClient BuildClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ShaiyaServerHub-AI/1.0");
            return c;
        }

        public string Name => "web_search";
        public string Description => "Cerca sul web mitigazioni, CVE, best-practice aggiornate su un " +
            "argomento di sicurezza/sistema (via server MCP SearXNG o endpoint REST, a seconda delle " +
            "Impostazioni). time_range opzionale ('day' per CVE/alert urgenti, 'week'/'month' per " +
            "best-practice, 'year' per riferimenti stabili). Read-only.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"query\":{\"type\":\"string\",\"description\":\"Testo della ricerca\"}," +
            "\"time_range\":{\"type\":\"string\",\"enum\":[\"day\",\"week\",\"month\",\"year\"],\"description\":" +
            "\"Filtro temporale: day per CVE/alert urgenti, week/month per best-practice, year per riferimenti stabili\"}}," +
            "\"required\":[\"query\"]}";

        public WebSearchTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string query = null, timeRange = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    if (doc.RootElement.TryGetProperty("query", out var q)) query = q.GetString();
                    if (doc.RootElement.TryGetProperty("time_range", out var tr)) timeRange = tr.GetString();
                }
                if (string.IsNullOrWhiteSpace(query)) return "ERRORE: parametro 'query' mancante.";
                if (string.IsNullOrWhiteSpace(_settings?.AiWebSearchUrl))
                    return "Ricerca web non configurata: imposta l'URL nelle Impostazioni (scheda AI).";

                bool rest = string.Equals(_settings.AiWebSearchMode, "Rest", StringComparison.OrdinalIgnoreCase);

                bool junkOnly, zeroResults;
                string result = rest ? SearchRest(query, timeRange, out junkOnly, out zeroResults) : SearchMcp(query, timeRange, out junkOnly, out zeroResults);

                // Auto-correzione DETERMINISTICA (non dipende dal modello): primo tentativo tutto social
                // + query contiene "shaiya" isolata → ritenta UNA volta senza quella parola.
                if (junkOnly && ShaiyaWord.IsMatch(query))
                {
                    string stripped = Regex.Replace(ShaiyaWord.Replace(query, ""), @"\s{2,}", " ").Trim();
                    if (!string.IsNullOrWhiteSpace(stripped) && !string.Equals(stripped, query, StringComparison.OrdinalIgnoreCase))
                    {
                        bool j2, z2;
                        string retry = rest ? SearchRest(stripped, timeRange, out j2, out z2) : SearchMcp(stripped, timeRange, out j2, out z2);
                        return $"(query originale \"{query}\" conteneva \"Shaiya\" — termine di nicchia gaming che da " +
                               $"solo pesca risultati social/marketing — ripetuta automaticamente come \"{stripped}\")\n\n" +
                               retry;
                    }
                }

                // Auto-correzione DETERMINISTICA: zero risultati sul set di default (che include motori
                // sospesi) → ritenta UNA volta forzando ESPLICITAMENTE i motori noti-attivi (bypassa
                // l'attesa sui sospesi); se la query aveva anche un site: troppo mirato lo toglie insieme
                // (verificato: la combinazione funziona, la sola rimozione del site: non basta).
                if (zeroResults && !junkOnly)
                {
                    bool hadSite = SiteOperator.IsMatch(query);
                    string retryQuery = hadSite
                        ? Regex.Replace(SiteOperator.Replace(query, ""), @"\s{2,}", " ").Trim()
                        : query;
                    if (!string.IsNullOrWhiteSpace(retryQuery))
                    {
                        bool j2, z2;
                        string retry = rest
                            ? SearchRest(retryQuery, timeRange, out j2, out z2, FallbackEngines)
                            : SearchMcp(retryQuery, timeRange, out j2, out z2, FallbackEngines);
                        string why = hadSite
                            ? $"query originale \"{query}\" con site: troppo mirato ha dato zero risultati sul set di " +
                              $"default — ripetuta su motori noti-attivi ({FallbackEngines}) come \"{retryQuery}\""
                            : $"query originale \"{query}\" ha dato zero risultati sul set di default (motori sospesi) " +
                              $"— ripetuta forzando i motori noti-attivi ({FallbackEngines})";
                        return $"({why})\n\n{retry}";
                    }
                }
                return result;
            }
            catch (Exception ex) { return "ERRORE ricerca web: " + ex.Message; }
        }

        private string SearchMcp(string query, string timeRange, out bool junkOnly, out bool zeroResults, string engines = null)
        {
            string resultText, error;
            if (McpSearxngClient.Search(_settings.AiWebSearchUrl, query, timeRange, out resultText, out junkOnly, out zeroResults, out error, engines))
                return string.IsNullOrWhiteSpace(resultText) ? "Nessun risultato." : resultText;
            zeroResults = false;
            return "ERRORE ricerca web (MCP): " + error;
        }

        private string SearchRest(string query, string timeRange, out bool junkOnly, out bool zeroResults, string engines = null)
        {
            junkOnly = false;
            zeroResults = false;
            string url = _settings.AiWebSearchUrl.TrimEnd('/') + "/search?format=json&q=" + Uri.EscapeDataString(query);
            if (timeRange == "day" || timeRange == "week" || timeRange == "month" || timeRange == "year")
                url += "&time_range=" + timeRange;
            if (!string.IsNullOrWhiteSpace(engines))
                url += "&engines=" + Uri.EscapeDataString(engines);

            using (var resp = Http.GetAsync(url).GetAwaiter().GetResult())
            {
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return "ERRORE 403: l'istanza SearXNG ha l'output JSON disabilitato. Abilitalo in " +
                           "settings.yml → search: formats: [html, json] e riavvia SearXNG.";
                if (!resp.IsSuccessStatusCode)
                    return $"ERRORE HTTP {(int)resp.StatusCode} da SearXNG: {Truncate(body, 300)}";

                using (var res = JsonDocument.Parse(body))
                {
                    if (!res.RootElement.TryGetProperty("results", out var results)) { zeroResults = true; return "Nessun risultato."; }
                    var sb = new StringBuilder();
                    int n = 0, junkFiltered = 0;
                    foreach (var item in results.EnumerateArray())
                    {
                        if (n >= 6) break;
                        string title = item.TryGetProperty("title", out var t) ? t.GetString() : "";
                        string urlR = item.TryGetProperty("url", out var u) ? u.GetString() : "";
                        string content = item.TryGetProperty("content", out var c) ? c.GetString() : "";
                        // Filtro social/marketing SEMPRE (vedi SearchResultFilter): stesso motivo del
                        // percorso MCP, non dipende da come il modello ha formulato la query.
                        if (SearchResultFilter.IsJunk(urlR)) { junkFiltered++; continue; }
                        n++;
                        sb.AppendLine($"- {title}\n  {urlR}\n  {content}");
                    }
                    if (n == 0 && junkFiltered > 0)
                    {
                        junkOnly = true;
                        return $"Nessun risultato utile — {junkFiltered} risultato/i erano solo social/marketing " +
                               "filtrati automaticamente. Per un termine di nicchia gaming come \"Shaiya\", una " +
                               "ricerca generica pesca quasi solo questo: usa site: mirato o rimuovi \"Shaiya\" " +
                               "dalla query se l'argomento è generico.";
                    }
                    if (junkFiltered > 0) sb.AppendLine($"\n({junkFiltered} risultato/i social/marketing filtrati)");
                    if (sb.Length == 0) { zeroResults = true; return "Nessun risultato."; }
                    return sb.ToString();
                }
            }
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
