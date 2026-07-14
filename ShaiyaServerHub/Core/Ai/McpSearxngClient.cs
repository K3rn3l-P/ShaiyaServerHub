using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>Filtro condiviso "rumore social" — vedi McpSearxngClient.FormatSearchPayload per il perché.</summary>
    public static class SearchResultFilter
    {
        // Domini quasi mai utili per ricerche tecniche/di sicurezza — soprattutto per query che
        // contengono "Shaiya" (termine di nicchia gaming): questi domini pescano marketing di server
        // privati, non contenuto tecnico. Filtro in CODICE (non nel prompt): un modello free debole
        // scelto a caso da openrouter/free non segue sempre istruzioni condizionali sulla query, ma
        // questo filtro si applica SEMPRE, indipendentemente da come il modello ha formulato la ricerca.
        private static readonly string[] JunkHosts =
        {
            "facebook.com", "tiktok.com", "instagram.com", "twitter.com", "x.com",
            "pinterest.com", "threads.net", "vk.com"
        };

        public static bool IsJunk(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                string host = new Uri(url).Host.ToLowerInvariant();
                foreach (var j in JunkHosts)
                    if (host == j || host.EndsWith("." + j, StringComparison.Ordinal)) return true;
            }
            catch { }
            return false;
        }
    }


    /// <summary>
    /// Client minimale per il server MCP "searxng" (transport HTTP streamable, JSON-RPC 2.0) —
    /// lo stesso protocollo/endpoint usato in questa sessione Claude Code per le ricerche web.
    /// Verificato manualmente contro l'istanza di questo ambiente (ihor-sokoliuk/mcp-searxng):
    /// handshake initialize → notifications/initialized → tools/call("searxng_web_search").
    /// Fa l'handshake completo ad OGNI ricerca (nessuna sessione persistita): l'uso è saltuario
    /// (chiamate AI on-demand), quindi robustezza &gt; risparmiare 2 round-trip.
    /// </summary>
    public static class McpSearxngClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private const string ProtocolVersion = "2025-06-18";

        /// <summary>
        /// Esegue una ricerca via MCP. Ritorna il testo già formattato. <paramref name="junkOnly"/> è
        /// true quando TUTTI i risultati erano social/marketing (vedi <see cref="SearchResultFilter"/>);
        /// <paramref name="zeroResults"/> è true quando non c'è ALCUN risultato utile (junk o no) — il
        /// chiamante (WebSearchTool) li usa per retry automatici DETERMINISTICI, non dipendenti dal
        /// modello che ha chiamato il tool.
        /// </summary>
        public static bool Search(string mcpUrl, string query, string timeRange, out string resultText,
            out bool junkOnly, out bool zeroResults, out string error, string engines = null)
        {
            resultText = null;
            junkOnly = false;
            zeroResults = false;
            error = null;
            try
            {
                // 1) initialize — il server risponde con l'Mcp-Session-Id da riusare nelle chiamate successive.
                string initBody = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "ShaiyaServerHub-AI", version = "1.0" }
                    }
                });
                string sessionId;
                var initResult = PostRpc(mcpUrl, initBody, null, out sessionId, out error);
                if (initResult == null) return false;

                // 2) notifications/initialized — notifica JSON-RPC (nessun "id"), risposta 202 vuota, ignorata.
                string notifyBody = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
                PostRaw(mcpUrl, notifyBody, sessionId);

                // 3) tools/call — la ricerca vera e propria. response_format="json" (invece del default
                // "text") fa sì che il server includa anche "unresponsive_engines": senza, un motore
                // sospeso (CAPTCHA/rate-limit/access-denied sull'IP condiviso) produce solo un generico
                // "No results found", indistinguibile da "la query non ha davvero risultati".
                var args = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["query"] = query, ["num_results"] = 6, ["response_format"] = "json"
                };
                if (timeRange == "day" || timeRange == "week" || timeRange == "month" || timeRange == "year")
                    args["time_range"] = timeRange;
                // Forza motori specifici invece del set di default: usato dal retry deterministico in
                // WebSearchTool quando il set di default (che include motori sospesi CAPTCHA/rate-limit)
                // torna zero risultati — interrogare SOLO i motori che rispondono davvero evita di
                // aspettare/contare su quelli sospesi in questo momento.
                if (!string.IsNullOrWhiteSpace(engines))
                    args["engines"] = engines;
                string callBody = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "tools/call",
                    @params = new { name = "searxng_web_search", arguments = args }
                });
                var callResult = PostRpc(mcpUrl, callBody, sessionId, out _, out error);
                if (callResult == null) return false;

                if (!(callResult.Value.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array &&
                      content.GetArrayLength() > 0 && content[0].TryGetProperty("text", out var textEl)))
                {
                    error = "Risposta MCP senza contenuto testuale.";
                    return false;
                }

                resultText = FormatSearchPayload(textEl.GetString(), out junkOnly, out zeroResults);
                return true;
            }
            catch (Exception ex) { error = "Errore MCP: " + ex.Message; return false; }
        }

        /// <summary>
        /// Il testo del tool (con response_format="json") è a sua volta una stringa JSON annidata:
        /// {"query","results":[{title,url,content,...}],"unresponsive_engines":[[nome,motivo],...]}.
        /// Formatta i risultati come testo leggibile E, se dei motori sono sospesi (CAPTCHA/rate-limit/
        /// access-denied — capita spesso su istanze SearXNG condivise il cui IP viene bloccato dai
        /// provider), lo dice esplicitamente: distingue "nessun risultato perché la ricerca non trova
        /// nulla" da "nessun risultato perché i motori principali sono momentaneamente bloccati" — così
        /// l'AI non conclude erroneamente che un argomento non abbia risultati pubblici.
        /// </summary>
        private static string FormatSearchPayload(string innerJson, out bool junkOnly, out bool zeroResults)
        {
            junkOnly = false;
            zeroResults = false;
            if (string.IsNullOrWhiteSpace(innerJson)) { zeroResults = true; return "Nessun risultato."; }
            try
            {
                using (var doc = JsonDocument.Parse(innerJson))
                {
                    var sb = new StringBuilder();
                    int n = 0;
                    int junkFiltered = 0;
                    if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in results.EnumerateArray())
                        {
                            if (n >= 6) break;
                            string title = item.TryGetProperty("title", out var t) ? t.GetString() : "";
                            string url = item.TryGetProperty("url", out var u) ? u.GetString() : "";
                            string content = item.TryGetProperty("content", out var c) ? c.GetString() : "";
                            // Filtro social/marketing SEMPRE, non dipende da come il modello ha formulato
                            // la query (vedi SearchResultFilter): un modello free debole non garantisce
                            // di evitare da solo risultati Facebook/TikTok per query tipo "Shaiya security".
                            if (SearchResultFilter.IsJunk(url)) { junkFiltered++; continue; }
                            n++;
                            sb.AppendLine($"- {title}\n  {url}\n  {content}");
                        }
                    }

                    string unresponsive = null;
                    if (doc.RootElement.TryGetProperty("unresponsive_engines", out var ue) && ue.ValueKind == JsonValueKind.Array && ue.GetArrayLength() > 0)
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        foreach (var pair in ue.EnumerateArray())
                        {
                            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) continue;
                            parts.Add($"{pair[0].GetString()} ({pair[1].GetString()})");
                        }
                        if (parts.Count > 0) unresponsive = string.Join(", ", parts);
                    }

                    if (n == 0)
                    {
                        zeroResults = true;
                        if (junkFiltered > 0)
                        {
                            junkOnly = true;
                            return $"Nessun risultato utile — {junkFiltered} risultato/i erano solo social/marketing " +
                                   "(Facebook/TikTok/Instagram/ecc., filtrati automaticamente) e non contenevano " +
                                   "informazione tecnica. Per un termine di nicchia gaming come \"Shaiya\", una " +
                                   "ricerca generica pesca quasi solo questo: se la domanda riguarda protocollo/Lua/" +
                                   "community, usa site: mirato alle fonti in knowledge/external-sources.md; se " +
                                   "riguarda best-practice di sicurezza generiche, cerca il termine tecnico SENZA " +
                                   "\"Shaiya\" nella query; se riguarda QUESTO server, usa i tool interni invece del web.";
                        }
                        if (unresponsive != null)
                            return "Nessun risultato — ATTENZIONE: i motori di ricerca principali sono " +
                                   $"momentaneamente sospesi su questo nodo SearXNG condiviso ({unresponsive}). " +
                                   "Non è detto che l'argomento non abbia risultati pubblici: è un blocco " +
                                   "temporaneo lato provider (CAPTCHA/rate-limit sull'IP condiviso), non un'assenza " +
                                   "reale di informazioni. IMPORTANTE: NON riprovare subito con una query riformulata " +
                                   "— il problema è il nodo di ricerca (quali motori rispondono in questo istante), " +
                                   "non la formulazione della domanda: riformulare non cambia quali motori sono " +
                                   "sospesi. Usa al massimo UN altro tentativo con parole diverse; se fallisce di " +
                                   "nuovo procedi con la knowledge base/i tool interni del server invece di continuare " +
                                   "a riprovare.";
                        return "Nessun risultato per questa ricerca.";
                    }

                    if (junkFiltered > 0)
                        sb.AppendLine($"\n({junkFiltered} risultato/i social/marketing filtrati automaticamente)");
                    if (unresponsive != null)
                        sb.AppendLine($"\n(Nota: risultati parziali — motori sospesi: {unresponsive})");
                    return sb.ToString();
                }
            }
            catch { return innerJson; }   // fallback: il testo grezzo è comunque meglio di niente
        }

        /// <summary>POST JSON-RPC, ritorna il JsonElement "result" (o null + error se fallisce). Cattura l'Mcp-Session-Id se presente.</summary>
        private static JsonElement? PostRpc(string url, string body, string sessionId, out string outSessionId, out string error)
        {
            outSessionId = sessionId;
            error = null;
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Headers.Add("Accept", "application/json, text/event-stream");
                if (!string.IsNullOrEmpty(sessionId)) req.Headers.Add("Mcp-Session-Id", sessionId);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                if (resp.Headers.TryGetValues("Mcp-Session-Id", out var sids))
                {
                    foreach (var s in sids) { outSessionId = s; break; }
                }
                string respBody = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    error = $"HTTP {(int)resp.StatusCode} dal server MCP: {Truncate(respBody, 300)}";
                    return null;
                }

                string json = ExtractSseData(respBody);
                if (string.IsNullOrWhiteSpace(json)) { error = "Risposta MCP vuota/non riconosciuta."; return null; }

                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("error", out var errEl))
                    {
                        error = "MCP error: " + (errEl.TryGetProperty("message", out var m) ? m.GetString() : errEl.GetRawText());
                        return null;
                    }
                    if (doc.RootElement.TryGetProperty("result", out var res)) return res.Clone();
                    error = "Risposta MCP senza 'result'.";
                    return null;
                }
            }
        }

        /// <summary>POST "fire and forget" per le notifiche JSON-RPC (nessuna risposta significativa attesa).</summary>
        private static void PostRaw(string url, string body, string sessionId)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Headers.Add("Accept", "application/json, text/event-stream");
                    if (!string.IsNullOrEmpty(sessionId)) req.Headers.Add("Mcp-Session-Id", sessionId);
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    Http.SendAsync(req).GetAwaiter().GetResult();
                }
            }
            catch { /* notifica best-effort */ }
        }

        // Il transport HTTP streamable MCP risponde in formato SSE ("event: message\ndata: {...}\n\n")
        // anche per una singola risposta non-stream: estrae il JSON dopo l'ultima riga "data: ".
        private static string ExtractSseData(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            string trimmed = body.TrimStart();
            if (trimmed.StartsWith("{")) return trimmed;   // già JSON puro (alcuni server non usano SSE)

            string last = null;
            foreach (var line in body.Replace("\r", "").Split('\n'))
                if (line.StartsWith("data:", StringComparison.Ordinal))
                    last = line.Substring(5).Trim();
            return last;
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
