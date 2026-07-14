using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Interroga il catalogo pubblico OpenRouter (`GET /api/v1/models`) per trovare il "miglior" modello
    /// FREE con tool-calling disponibile ORA — OpenRouter aggiunge/rimuove modelli free di continuo,
    /// un ID pinnato a mano (vedi ModelRouter) può sparire o essere superato da uno migliore col tempo.
    /// Nessuna euristica sul CONTENUTO delle risposte (impossibile misurare "allucinazione" da API): usa
    /// solo segnali oggettivi esposti da OpenRouter stesso — popolarità reale (`sort=top-weekly`, token
    /// processati nell'ultima settimana: proxy per "la community lo trova affidabile per uso agentico")
    /// combinata con velocità (`sort=throughput-high-to-low`, p50 token/s) via rank-sum, così non si
    /// sceglie né il più popolare-ma-lento né il più veloce-ma-oscuro. Filtra sempre `supported_parameters=
    /// tools` (altrimenti l'AgentBrain non potrebbe usare i tool) e prezzo prompt/completion a "0".
    /// Sincrono di proposito (pattern OpenRouterClient/ScriptRunner): chiamare sempre da thread di background.
    /// </summary>
    public static class OpenRouterModelCatalog
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public class ModelInfo
        {
            public string Id;
            public string Name;
            public long ContextLength;
        }

        public class RankedModel
        {
            public string Id;
            public string Name;
            public long ContextLength;
            public int RankPopularity;   // 0-based: 0 = più popolare
            public int RankSpeed;        // 0-based: 0 = più veloce
            public string Note;          // motivazione leggibile (mostrata in Impostazioni)
        }

        /// <summary>
        /// Classifica TUTTI i candidati free/tool-calling per rank-sum (popolarità+velocità), dal
        /// migliore al peggiore. Usata per scegliere sia Default (il migliore) sia Fallback (il
        /// migliore DIVERSO dal Default — mai la lotteria `openrouter/free` come primo paracadute,
        /// quella resta un terzo livello aggiunto da `AgentBrain` solo come rete di sicurezza estrema).
        /// </summary>
        public static bool FindRankedFreeToolModels(HubSettings settings, out List<RankedModel> ranked, out string error)
        {
            ranked = new List<RankedModel>();
            error = null;
            try
            {
                var popular = FetchFreeToolModels(settings, "top-weekly", out error);
                if (error != null) return false;
                if (popular.Count == 0) { error = "Nessun modello free con tool-calling trovato su OpenRouter."; return false; }

                var fast = FetchFreeToolModels(settings, "throughput-high-to-low", out error);
                if (error != null) return false;

                var fastRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < fast.Count; i++)
                    if (!fastRank.ContainsKey(fast[i].Id)) fastRank[fast[i].Id] = i;

                for (int i = 0; i < popular.Count; i++)
                {
                    int rf = fastRank.TryGetValue(popular[i].Id, out var r) ? r : fast.Count;
                    ranked.Add(new RankedModel
                    {
                        Id = popular[i].Id,
                        Name = popular[i].Name,
                        ContextLength = popular[i].ContextLength,
                        RankPopularity = i,
                        RankSpeed = rf,
                        Note = $"{popular[i].Name} — popolarità #{i + 1}, velocità #{rf + 1} " +
                               $"(su {popular.Count} modelli free/tool-calling), ctx {popular[i].ContextLength:N0}"
                    });
                }
                ranked = ranked.OrderBy(r => r.RankPopularity + r.RankSpeed).ToList();
                return true;
            }
            catch (Exception ex) { error = "Errore catalogo modelli OpenRouter: " + ex.Message; return false; }
        }

        /// <summary>Scorciatoia: solo il primo classificato (compat con chiamate esistenti a 1 risultato).</summary>
        public static bool FindBestFreeToolModel(HubSettings settings, out string bestId, out string note, out string error)
        {
            bestId = null; note = null;
            if (!FindRankedFreeToolModels(settings, out var ranked, out error) || ranked.Count == 0) return false;
            bestId = ranked[0].Id; note = ranked[0].Note; return true;
        }

        private static List<ModelInfo> FetchFreeToolModels(HubSettings settings, string sort, out string error)
        {
            error = null;
            var list = new List<ModelInfo>();
            string url = $"https://openrouter.ai/api/v1/models?supported_parameters=tools&sort={sort}";
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (!string.IsNullOrWhiteSpace(settings?.OpenRouterApiKey))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenRouterApiKey);

                var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    error = $"HTTP {(int)resp.StatusCode}: {Truncate(body, 200)}";
                    return list;
                }

                using (var doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                        return list;

                    foreach (var m in data.EnumerateArray())
                    {
                        string id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        if (string.IsNullOrEmpty(id) || !id.EndsWith(":free", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string promptPrice = "0";
                        if (m.TryGetProperty("pricing", out var pricing) &&
                            pricing.TryGetProperty("prompt", out var pp) && pp.ValueKind == JsonValueKind.String)
                            promptPrice = pp.GetString();
                        if (promptPrice != "0") continue;

                        list.Add(new ModelInfo
                        {
                            Id = id,
                            Name = m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : id,
                            ContextLength = m.TryGetProperty("context_length", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0
                        });
                    }
                }
            }
            return list;
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
