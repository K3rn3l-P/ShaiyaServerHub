using System;
using System.Net.Http;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Helper Telegram Bot API. In P1 usato SOLO per rilevare il Chat ID dal bot token (comodità
    /// Impostazioni): l'utente crea il bot con @BotFather, gli scrive un messaggio, il Hub chiama
    /// getUpdates e legge il chat.id dell'ultimo aggiornamento. Il bot 2-vie (notifiche/approvazioni)
    /// arriva in P2 (TelegramClient).
    /// </summary>
    public static class TelegramHelper
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>
        /// Rileva il Chat ID dall'ultimo messaggio ricevuto dal bot (offset=-1&amp;limit=1 = "solo
        /// l'ultimo aggiornamento", niente long-polling). Richiede che l'utente abbia GIÀ scritto
        /// almeno un messaggio al bot su Telegram prima di premere il pulsante.
        /// </summary>
        public static bool DetectChatId(string botToken, out string chatId, out string error)
        {
            chatId = null;
            error = null;
            if (string.IsNullOrWhiteSpace(botToken)) { error = "Bot token mancante."; return false; }

            try
            {
                string url = $"https://api.telegram.org/bot{Uri.EscapeDataString(botToken)}/getUpdates?offset=-1&limit=1";
                using (var resp = Http.GetAsync(url).GetAwaiter().GetResult())
                {
                    string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    using (var doc = JsonDocument.Parse(body))
                    {
                        var root = doc.RootElement;
                        bool ok = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
                        if (!ok)
                        {
                            string desc = root.TryGetProperty("description", out var d) ? d.GetString() : $"HTTP {(int)resp.StatusCode}";
                            // 409 tipico: un webhook è attivo su questo bot → getUpdates è in conflitto.
                            error = desc != null && desc.IndexOf("webhook", StringComparison.OrdinalIgnoreCase) >= 0
                                ? "Il bot ha un webhook attivo: rimuovilo (o usane uno nuovo) per rilevare il Chat ID via getUpdates."
                                : $"Telegram: {desc} (token corretto?)";
                            return false;
                        }

                        var result = root.GetProperty("result");
                        if (result.GetArrayLength() == 0)
                        {
                            error = "Nessun messaggio trovato: apri Telegram, cerca il tuo bot e scrivigli qualcosa (es. /start), poi riprova.";
                            return false;
                        }

                        var update = result[result.GetArrayLength() - 1];
                        JsonElement chat;
                        if (update.TryGetProperty("message", out var msg) && msg.TryGetProperty("chat", out chat))
                        {
                            chatId = chat.GetProperty("id").GetRawText();
                            return true;
                        }
                        error = "Aggiornamento trovato ma senza un messaggio di chat valido — scrivi un messaggio testuale al bot e riprova.";
                        return false;
                    }
                }
            }
            catch (Exception ex) { error = "Errore: " + ex.Message; return false; }
        }
    }
}
