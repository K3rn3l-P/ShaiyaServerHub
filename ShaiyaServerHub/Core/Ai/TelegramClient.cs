using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>Un click su un bottone inline Telegram, già scomposto (vedi callback_data "decisione:id").</summary>
    public class TelegramCallback
    {
        public string Decision;          // "approve" | "deny" | "takeover"
        public string ActionId;          // Id di ActionQueue.PendingAction
        public string CallbackQueryId;   // per answerCallbackQuery (toglie lo spinner sul telefono)
        public string FromUser;
    }

    /// <summary>Un messaggio di testo libero (non un click su bottone) — per la chat 2 vie via Telegram.</summary>
    public class TelegramTextMessage
    {
        public string Text;
        public string FromUser;
        public string ChatId;   // confrontato con HubSettings.TelegramChatId prima di processarlo — MAI rispondere a chat non configurate
    }

    /// <summary>
    /// Bot Telegram 2-vie — vedi PLAN §Componenti codice. A differenza di <see cref="TelegramHelper"/>
    /// (solo rilevamento Chat ID, uso singolo in Impostazioni), questa classe invia messaggi CON bottoni
    /// inline (Approva/Nega/Prendo io) e fa polling per leggerne il click — usata da
    /// <see cref="ApprovalWatchdog"/> per il ciclo di approvazione delle azioni T2+.
    /// </summary>
    public static class TelegramClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // Offset getUpdates: in-process, un solo poller alla volta (ApprovalWatchdog). Al primo giro può
        // includere un piccolo backlog di update non ancora confermati (es. da test precedenti di
        // TelegramHelper.DetectChatId) — innocuo: i callback che non matchano un ActionQueue.Id pendente
        // vengono semplicemente ignorati da ApprovalWatchdog.
        private static long _lastUpdateId;

        public static bool SendApprovalRequest(string botToken, string chatId, string text, string actionId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            { error = "Telegram non configurato (token/chat ID mancanti)."; return false; }

            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["chat_id"] = chatId,
                    ["text"] = text,
                    ["reply_markup"] = new Dictionary<string, object>
                    {
                        ["inline_keyboard"] = new object[]
                        {
                            new object[]
                            {
                                new Dictionary<string, object> { ["text"] = "✅ Approva", ["callback_data"] = "approve:" + actionId },
                                new Dictionary<string, object> { ["text"] = "❌ Nega", ["callback_data"] = "deny:" + actionId },
                                new Dictionary<string, object> { ["text"] = "🙋 Prendo io", ["callback_data"] = "takeover:" + actionId }
                            }
                        }
                    }
                };
                return PostOk(botToken, "sendMessage", payload, out error);
            }
            catch (Exception ex) { error = "Errore invio Telegram: " + ex.Message; return false; }
        }

        public static bool SendInfo(string botToken, string chatId, string text, out string error, string parseMode = null)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            { error = "Telegram non configurato."; return false; }
            try
            {
                var payload = new Dictionary<string, object> { ["chat_id"] = chatId, ["text"] = text };
                if (!string.IsNullOrEmpty(parseMode)) payload["parse_mode"] = parseMode;
                return PostOk(botToken, "sendMessage", payload, out error);
            }
            catch (Exception ex) { error = "Errore invio Telegram: " + ex.Message; return false; }
        }

        /// <summary>
        /// Legge gli update arrivati dall'ultimo giro (long-poll breve, timeout=0: non blocca) — sia i
        /// click sui bottoni (callback_query, valore di ritorno) sia i messaggi di testo libero
        /// (message.text, out param) nella STESSA chiamata: un secondo poller indipendente
        /// consumerebbe/perderebbe update dell'altro (getUpdates avanza l'offset condiviso), per questo
        /// un'unica chiamata gestisce entrambi i tipi.
        /// </summary>
        public static List<TelegramCallback> PollCallbacks(string botToken, out List<TelegramTextMessage> textMessages, out string error)
        {
            error = null;
            var result = new List<TelegramCallback>();
            textMessages = new List<TelegramTextMessage>();
            if (string.IsNullOrWhiteSpace(botToken)) { error = "Bot token mancante."; return result; }

            try
            {
                string url = $"https://api.telegram.org/bot{Uri.EscapeDataString(botToken)}/getUpdates?offset={_lastUpdateId + 1}&timeout=0";
                using (var resp = Http.GetAsync(url).GetAwaiter().GetResult())
                {
                    string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    using (var doc = JsonDocument.Parse(body))
                    {
                        if (!(doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean()))
                        {
                            error = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : "errore getUpdates";
                            return result;
                        }
                        foreach (var upd in doc.RootElement.GetProperty("result").EnumerateArray())
                        {
                            if (upd.TryGetProperty("update_id", out var uid))
                                _lastUpdateId = Math.Max(_lastUpdateId, uid.GetInt64());

                            if (upd.TryGetProperty("message", out var msgEl))
                            {
                                string text = msgEl.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
                                // "/start" è il messaggio automatico che Telegram invia al primo tap su
                                // "Avvia" — l'unico comando bot-standard da ignorare in silenzio, tutti gli
                                // altri (compresi i comandi custom in TelegramCommands e testo libero)
                                // vanno instradati da ProcessTelegramMessages.
                                if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("/start"))
                                {
                                    string chatId = msgEl.TryGetProperty("chat", out var chatEl) && chatEl.TryGetProperty("id", out var chatIdEl)
                                        ? chatIdEl.GetInt64().ToString() : null;
                                    string fromUserMsg = msgEl.TryGetProperty("from", out var fromElMsg) && fromElMsg.TryGetProperty("username", out var unMsg)
                                        ? unMsg.GetString() : null;
                                    textMessages.Add(new TelegramTextMessage { Text = text, FromUser = fromUserMsg, ChatId = chatId });
                                }
                                continue;
                            }

                            if (!upd.TryGetProperty("callback_query", out var cq)) continue;
                            string data = cq.TryGetProperty("data", out var dEl) ? dEl.GetString() : null;
                            if (string.IsNullOrEmpty(data)) continue;

                            int sep = data.IndexOf(':');
                            if (sep <= 0) continue;

                            result.Add(new TelegramCallback
                            {
                                Decision = data.Substring(0, sep),
                                ActionId = data.Substring(sep + 1),
                                CallbackQueryId = cq.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
                                FromUser = cq.TryGetProperty("from", out var fromEl) && fromEl.TryGetProperty("username", out var un)
                                    ? un.GetString() : null
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { error = "Errore polling Telegram: " + ex.Message; }
            return result;
        }

        /// <summary>
        /// Indicatore "sta scrivendo…" — dura al massimo ~5s lato Telegram (docs ufficiali), va
        /// ripetuto durante operazioni lunghe. Cosmetico, best-effort: mai deve bloccare/rompere il turno
        /// di chat reale se fallisce.
        /// </summary>
        public static void SendTyping(string botToken, string chatId)
        {
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId)) return;
            try
            {
                var payload = new Dictionary<string, object> { ["chat_id"] = chatId, ["action"] = "typing" };
                PostOk(botToken, "sendChatAction", payload, out _);
            }
            catch { /* cosmetico */ }
        }

        /// <summary>
        /// Registra il menu comandi (autocomplete "/" + bottone Menu nella chat) — vedi
        /// <see cref="TelegramCommands"/>. Chiamata idempotente, va rifatta solo se cambia l'elenco.
        /// </summary>
        public static bool SetMyCommands(string botToken, IEnumerable<TelegramCommands.Entry> commands, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(botToken)) { error = "Bot token mancante."; return false; }
            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["commands"] = commands.Select(c => new Dictionary<string, object>
                    {
                        ["command"] = c.Command,
                        ["description"] = c.Description
                    }).ToArray()
                };
                return PostOk(botToken, "setMyCommands", payload, out error);
            }
            catch (Exception ex) { error = "Errore setMyCommands: " + ex.Message; return false; }
        }

        /// <summary>Toglie lo spinner di caricamento sul bottone premuto (cosmetico, best-effort).</summary>
        public static void AnswerCallback(string botToken, string callbackQueryId, string text)
        {
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(callbackQueryId)) return;
            try
            {
                var payload = new Dictionary<string, object>
                { ["callback_query_id"] = callbackQueryId, ["text"] = text ?? "", ["show_alert"] = false };
                PostOk(botToken, "answerCallbackQuery", payload, out _);
            }
            catch { /* cosmetico, non deve mai bloccare il flusso reale */ }
        }

        private static bool PostOk(string botToken, string method, Dictionary<string, object> payload, out string error)
        {
            error = null;
            string json = JsonSerializer.Serialize(payload);
            string url = $"https://api.telegram.org/bot{Uri.EscapeDataString(botToken)}/{method}";
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var resp = Http.PostAsync(url, content).GetAwaiter().GetResult())
            {
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using (var doc = JsonDocument.Parse(body))
                {
                    bool ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
                    if (!ok)
                        error = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : $"HTTP {(int)resp.StatusCode}";
                    return ok;
                }
            }
        }
    }
}
