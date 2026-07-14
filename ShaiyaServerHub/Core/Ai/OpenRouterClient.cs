using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>Un messaggio della conversazione (stile OpenAI chat completions).</summary>
    public class OpenRouterMessage
    {
        public string Role;              // "system" | "user" | "assistant" | "tool"
        public string Content;
        public string ToolCallId;        // solo per Role == "tool"
        public string Name;              // solo per Role == "tool" (nome del tool)
        public List<OpenRouterToolCall> ToolCalls;   // solo per Role == "assistant" con tool-calling
    }

    public class OpenRouterToolCall
    {
        public string Id;
        public string Name;
        public string ArgumentsJson;
    }

    public class OpenRouterResult
    {
        public string Content;
        public List<OpenRouterToolCall> ToolCalls = new List<OpenRouterToolCall>();
        public string FinishReason;
        /// <summary>Modello che ha effettivamente risposto (utile quando si passa una lista con fallback).</summary>
        public string ModelUsed;
        public string Error;
        /// <summary>True se la richiesta è stata annullata dall'utente (pulsante Stop), non un errore vero.</summary>
        public bool Cancelled;
        public bool Ok => Error == null && !Cancelled;
    }

    /// <summary>
    /// Client HTTP verso OpenRouter (chat completions + tool-calling). Sincrono di proposito (come
    /// ScriptRunner/ProcessRunner): va sempre chiamato da un thread di background (Task.Run), mai dalla UI.
    /// </summary>
    public class OpenRouterClient
    {
        private readonly HubSettings _settings;
        // Modelli free condivisi possono avere latenza alta nei picchi (docs OpenRouter) e i turni con
        // molti tool-result nel contesto (audit multi-sensore) richiedono generazioni più lunghe:
        // 90s si è rivelato insufficiente (osservato: timeout scambiato per Stop utente mai premuto).
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(150) };

        public OpenRouterClient(HubSettings settings) { _settings = settings; }

        /// <summary>
        /// <paramref name="models"/> in ordine di priorità: OpenRouter prova il primo e, se il provider
        /// è down/rate-limited/rifiuta per moderazione, passa AUTOMATICAMENTE al successivo lato server
        /// (parametro nativo "models", un'unica richiesta HTTP) — vedi
        /// https://openrouter.ai/docs/guides/routing/model-fallbacks. Più robusto ed efficiente di un
        /// retry manuale client-side con due chiamate separate.
        /// </summary>
        public OpenRouterResult ChatCompletion(
            IEnumerable<string> models,
            IEnumerable<OpenRouterMessage> messages,
            IEnumerable<(string Name, string Description, string ParametersJsonSchema)> tools,
            CancellationToken ct = default)
        {
            var result = new OpenRouterResult();
            var modelList = (models ?? Enumerable.Empty<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (modelList.Count == 0) { result.Error = "Nessun modello configurato."; return result; }

            if (string.IsNullOrWhiteSpace(_settings?.OpenRouterApiKey))
            {
                result.Error = "API key OpenRouter non configurata (Impostazioni AI).";
                return result;
            }
            if (string.IsNullOrWhiteSpace(_settings?.OpenRouterEndpoint))
            {
                result.Error = "Endpoint OpenRouter non configurato.";
                return result;
            }

            string json;
            try { json = BuildPayload(modelList, messages, tools); }
            catch (Exception ex) { result.Error = "Payload non valido: " + ex.Message; return result; }

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; return result; }
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Post, _settings.OpenRouterEndpoint))
                    {
                        req.Headers.Add("Authorization", "Bearer " + _settings.OpenRouterApiKey);
                        req.Headers.Add("HTTP-Referer", "https://shaiyaserverhub.local");
                        req.Headers.Add("X-Title", "ShaiyaServerHub AI");
                        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                        var resp = Http.SendAsync(req, ct).GetAwaiter().GetResult();
                        string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                        if (!resp.IsSuccessStatusCode)
                        {
                            // 429/5xx: transitorio, riprova con backoff breve. Altri: fallisci subito.
                            bool transient = (int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500;
                            if (transient && attempt < maxAttempts) { Thread.Sleep(500 * attempt); continue; }
                            result.Error = $"HTTP {(int)resp.StatusCode}: {Truncate(body, 500)}";
                            return result;
                        }

                        return ParseResponse(body);
                    }
                }
                catch (OperationCanceledException)
                {
                    // ct.IsCancellationRequested = vero Stop dell'utente. Altrimenti è il timeout
                    // INTERNO di HttpClient (stesso tipo di eccezione, causa diversa) — prima veniva
                    // etichettato "annullato dall'utente" anche quando nessuno aveva premuto Stop,
                    // fuorviante. Trattalo come gli altri errori transitori: riprova con backoff.
                    if (ct.IsCancellationRequested) { result.Cancelled = true; return result; }
                    if (attempt < maxAttempts) { Thread.Sleep(500 * attempt); continue; }
                    result.Error = $"Timeout: il modello non ha risposto entro {Http.Timeout.TotalSeconds:0}s " +
                                    "(contesto grande o provider free lento/sovraccarico). Riprova, magari con meno controlli per volta.";
                    return result;
                }
                catch (Exception ex)
                {
                    if (attempt < maxAttempts) { Thread.Sleep(500 * attempt); continue; }
                    result.Error = ex.Message;
                    return result;
                }
            }
            result.Error = "Richiesta fallita dopo i tentativi previsti.";
            return result;
        }

        /// <summary>
        /// Come <see cref="ChatCompletion"/> ma in streaming (SSE, `stream: true`) — invoca
        /// <paramref name="onTextDelta"/> ad ogni frammento di testo generato, così la UI può mostrare
        /// la risposta man mano invece di aspettare l'intero turno (era la lamentela: "aspetto tutto il
        /// riepilogo prima di vedere qualcosa"). I tool_calls (se il modello li chiede) arrivano anch'essi
        /// a pezzi (delta.tool_calls, accumulati per "index") ma non c'è nulla da mostrare finché non sono
        /// completi — <see cref="AgentBrain"/> li esegue solo a fine turno come nel path non-streaming.
        /// Formato SSE standard OpenAI-compatible: righe `data: {...}` (JSON per chunk), `data: [DONE]`
        /// a fine stream, righe che iniziano per `:` sono commenti keep-alive da ignorare (vedi
        /// https://openrouter.ai/docs/api/reference/streaming).
        /// </summary>
        public OpenRouterResult ChatCompletionStreaming(
            IEnumerable<string> models,
            IEnumerable<OpenRouterMessage> messages,
            IEnumerable<(string Name, string Description, string ParametersJsonSchema)> tools,
            Action<string> onTextDelta,
            CancellationToken ct = default)
        {
            var result = new OpenRouterResult();
            var modelList = (models ?? Enumerable.Empty<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (modelList.Count == 0) { result.Error = "Nessun modello configurato."; return result; }

            if (string.IsNullOrWhiteSpace(_settings?.OpenRouterApiKey))
            {
                result.Error = "API key OpenRouter non configurata (Impostazioni AI).";
                return result;
            }
            if (string.IsNullOrWhiteSpace(_settings?.OpenRouterEndpoint))
            {
                result.Error = "Endpoint OpenRouter non configurato.";
                return result;
            }

            string json;
            try { json = BuildPayload(modelList, messages, tools, stream: true); }
            catch (Exception ex) { result.Error = "Payload non valido: " + ex.Message; return result; }

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; return result; }
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Post, _settings.OpenRouterEndpoint))
                    {
                        req.Headers.Add("Authorization", "Bearer " + _settings.OpenRouterApiKey);
                        req.Headers.Add("HTTP-Referer", "https://shaiyaserverhub.local");
                        req.Headers.Add("X-Title", "ShaiyaServerHub AI");
                        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                        using (var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult())
                        {
                            if (!resp.IsSuccessStatusCode)
                            {
                                string errBody = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                bool transient = (int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500;
                                if (transient && attempt < maxAttempts) { Thread.Sleep(500 * attempt); continue; }
                                result.Error = $"HTTP {(int)resp.StatusCode}: {Truncate(errBody, 500)}";
                                return result;
                            }

                            using (var stream = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                ParseSseInto(reader, result, onTextDelta, ct);
                                if (result.Cancelled) return result;
                                return result;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested) { result.Cancelled = true; return result; }
                    if (attempt < maxAttempts) { Thread.Sleep(500 * attempt); continue; }
                    result.Error = $"Timeout: il modello non ha risposto entro {Http.Timeout.TotalSeconds:0}s " +
                                    "(contesto grande o provider free lento/sovraccarico). Riprova, magari con meno controlli per volta.";
                    return result;
                }
                catch (Exception ex)
                {
                    if (attempt < maxAttempts) { Thread.Sleep(500 * attempt); continue; }
                    result.Error = ex.Message;
                    return result;
                }
            }
            result.Error = "Richiesta fallita dopo i tentativi previsti.";
            return result;
        }

        /// <summary>Legge lo stream SSE riga per riga e popola <paramref name="result"/> in-place.</summary>
        private static void ParseSseInto(StreamReader reader, OpenRouterResult result, Action<string> onTextDelta, CancellationToken ct)
        {
            var sbContent = new StringBuilder();
            // Tool call accumulati per "index" (arrivano a pezzi: id/name nel primo chunk, "arguments"
            // concatenato su più chunk successivi — stesso pattern OpenAI standard).
            var toolCallIds = new Dictionary<int, string>();
            var toolCallNames = new Dictionary<int, string>();
            var toolCallArgs = new Dictionary<int, StringBuilder>();
            var order = new List<int>();

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; return; }
                if (line.Length == 0 || line[0] == ':') continue;     // riga vuota o commento keep-alive
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string payload = line.Substring(5).Trim();
                if (payload == "[DONE]") break;
                if (payload.Length == 0) continue;

                JsonDocument chunk;
                try { chunk = JsonDocument.Parse(payload); }
                catch { continue; } // riga malformata/parziale: ignora, non è fatale per lo stream

                using (chunk)
                {
                    var root = chunk.RootElement;
                    if (root.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String)
                        result.ModelUsed = mEl.GetString();
                    if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                    var choice = choices[0];
                    if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                        result.FinishReason = fr.GetString();
                    if (!choice.TryGetProperty("delta", out var delta)) continue;

                    if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        string piece = c.GetString();
                        if (!string.IsNullOrEmpty(piece)) { sbContent.Append(piece); onTextDelta?.Invoke(piece); }
                    }

                    if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcs.EnumerateArray())
                        {
                            int idx = tc.TryGetProperty("index", out var ix) && ix.ValueKind == JsonValueKind.Number ? ix.GetInt32() : 0;
                            if (!toolCallArgs.ContainsKey(idx))
                            {
                                toolCallArgs[idx] = new StringBuilder();
                                order.Add(idx);
                            }
                            if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                                toolCallIds[idx] = idEl.GetString();
                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                                    toolCallNames[idx] = nEl.GetString();
                                if (fn.TryGetProperty("arguments", out var aEl) && aEl.ValueKind == JsonValueKind.String)
                                    toolCallArgs[idx].Append(aEl.GetString());
                            }
                        }
                    }
                }
            }

            result.Content = sbContent.Length > 0 ? sbContent.ToString() : null;
            foreach (var idx in order)
            {
                result.ToolCalls.Add(new OpenRouterToolCall
                {
                    Id = toolCallIds.TryGetValue(idx, out var id) ? id : Guid.NewGuid().ToString("N"),
                    Name = toolCallNames.TryGetValue(idx, out var n) ? n : null,
                    ArgumentsJson = toolCallArgs[idx].ToString()
                });
            }
        }

        private static string BuildPayload(List<string> models, IEnumerable<OpenRouterMessage> messages,
            IEnumerable<(string Name, string Description, string ParametersJsonSchema)> tools, bool stream = false)
        {
            // Un solo modello: campo "model" (stile OpenAI standard). Più modelli: campo "models"
            // (fallback nativo OpenRouter, provato in ordine lato server).
            var payload = new Dictionary<string, object>
            {
                ["messages"] = messages.Select(MessageToJson).ToList()
            };
            if (models.Count == 1) payload["model"] = models[0];
            else payload["models"] = models;
            if (stream) payload["stream"] = true;

            var toolList = tools?.ToList();
            if (toolList != null && toolList.Count > 0)
            {
                payload["tools"] = toolList.Select(t => new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = JsonDocument.Parse(
                            string.IsNullOrWhiteSpace(t.ParametersJsonSchema)
                                ? "{\"type\":\"object\",\"properties\":{}}"
                                : t.ParametersJsonSchema).RootElement
                    }
                }).ToList();
            }

            return JsonSerializer.Serialize(payload);
        }

        private static object MessageToJson(OpenRouterMessage m)
        {
            var d = new Dictionary<string, object> { ["role"] = m.Role };
            if (m.Content != null) d["content"] = m.Content;
            if (m.Role == "tool")
            {
                d["tool_call_id"] = m.ToolCallId;
                d["name"] = m.Name;
            }
            if (m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                d["tool_calls"] = m.ToolCalls.Select(tc => new Dictionary<string, object>
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object> { ["name"] = tc.Name, ["arguments"] = tc.ArgumentsJson }
                }).ToList();
            }
            return d;
        }

        private static OpenRouterResult ParseResponse(string body)
        {
            var result = new OpenRouterResult();
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("model", out var mu)) result.ModelUsed = mu.GetString();
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() == 0) { result.Error = "Risposta senza 'choices'."; return result; }
                    var choice = choices[0];
                    var message = choice.GetProperty("message");
                    result.FinishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

                    if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                        result.Content = c.GetString();

                    if (message.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcs.EnumerateArray())
                        {
                            var fn = tc.GetProperty("function");
                            result.ToolCalls.Add(new OpenRouterToolCall
                            {
                                Id = tc.TryGetProperty("id", out var id) ? id.GetString() : Guid.NewGuid().ToString("N"),
                                Name = fn.GetProperty("name").GetString(),
                                ArgumentsJson = fn.TryGetProperty("arguments", out var a) ? a.GetString() : "{}"
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { result.Error = "Risposta non valida: " + ex.Message; }
            return result;
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
