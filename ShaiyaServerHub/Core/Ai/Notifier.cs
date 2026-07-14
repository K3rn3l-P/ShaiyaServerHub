using System;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Facade sopra <see cref="TelegramClient"/> — formatta i messaggi e verifica la configurazione,
    /// così il resto del codice (PolicyGate/ApprovalWatchdog) non parla mai direttamente col trasporto
    /// Telegram. Unico canale oggi; se in futuro se ne aggiungono altri (email, webhook), questo è il
    /// punto in cui si diramano senza toccare i chiamanti.
    /// </summary>
    public static class Notifier
    {
        public static bool IsConfigured(HubSettings settings) =>
            settings != null && !string.IsNullOrWhiteSpace(settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(settings.TelegramChatId);

        /// <summary>
        /// Notifica informativa, nessun bottone (es. esito di un'azione già decisa/eseguita, digest
        /// automatico, risposta chat Telegram). Il testo passa SEMPRE da <see cref="TelegramFormatter"/>
        /// (markdown → HTML Telegram) — bug reale corretto: il testo generato dall'LLM (digest/esiti)
        /// arrivava con `**`/`#` letterali, MarkdownV2 di Telegram è troppo fragile per testo libero (un
        /// carattere non escapato rompe l'intero messaggio). No-op visivo su testo già semplice (nessun
        /// tag da convertire).
        ///
        /// Bug reale corretto: un errore di invio (HTML malformato da markdown irregolare del modello,
        /// o testo oltre il limite Telegram di 4096 caratteri — MAI troncato prima d'ora) veniva scartato
        /// in silenzio (<c>out _</c>) — l'utente vedeva il turno completarsi nel log ma NESSUN messaggio
        /// arrivava su Telegram, senza errore da nessuna parte. Ora: (1) troncamento di sicurezza PRIMA
        /// della conversione HTML (margine ampio sotto 4096, la crescita per i tag è modesta); (2) se
        /// l'invio HTML fallisce comunque, log dell'errore + fallback automatico a testo semplice (senza
        /// parse_mode) — garantisce la consegna anche se l'HTML generato è malformato (tag sovrapposti da
        /// markdown non bilanciato, un rischio reale con testo generato da un LLM).
        /// </summary>
        public static void NotifyInfo(HubSettings settings, string text)
        {
            if (!IsConfigured(settings)) return;
            string safe = Truncate(text, 3500);
            string html = TelegramFormatter.ToTelegramHtml(safe);
            if (TelegramClient.SendInfo(settings.TelegramBotToken, settings.TelegramChatId, html, out var error, "HTML"))
                return;

            App.WriteLog("AI-TELEGRAM", $"invio HTML fallito ({error}) — ritento come testo semplice.");
            if (!TelegramClient.SendInfo(settings.TelegramBotToken, settings.TelegramChatId, safe, out var error2))
                App.WriteLog("AI-TELEGRAM", "invio fallback testo semplice FALLITO: " + error2);
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";

        /// <summary>
        /// Notifica del digest esteso (P4), con prefisso per gravità — "critico" per High, "warning" per
        /// Medium (Low non arriva mai qui: il Tick del digest già filtra prima di chiamare questo metodo,
        /// vedi SecurityDigestScheduler). Stesso trasporto di NotifyInfo, solo formattazione diversa.
        /// </summary>
        public static void NotifyDigest(HubSettings settings, FindingSeverity severity, string body)
        {
            string label = severity == FindingSeverity.High ? "🚨 critico" : "⚠️ warning";
            NotifyInfo(settings, $"🩺 Digest automatico [{label}]\n{body}");
        }

        /// <summary>Richiesta di approvazione con bottoni Approva/Nega/Prendo io per una PendingAction.</summary>
        public static bool NotifyApprovalRequest(HubSettings settings, PendingAction action, string humanSummary, out string error)
        {
            error = null;
            if (!IsConfigured(settings)) { error = "Telegram non configurato (Impostazioni → scheda AI)."; return false; }

            string graceText = action.GraceDeadline.HasValue
                ? $"\n⏱ Auto-eseguita tra {(action.GraceDeadline.Value - DateTime.UtcNow).TotalMinutes:0.#} min se nessuno risponde."
                : "\n⏳ Richiede una decisione esplicita (nessuna esecuzione automatica).";

            string text = $"🤖 ShaiyaServerHub AI — richiesta approvazione [{action.Id}]\n\n" +
                          $"Azione: {action.ToolName}\n" +
                          $"Dettagli: {humanSummary}\n" +
                          $"Motivazione: {action.Reasoning}" +
                          graceText;

            return TelegramClient.SendApprovalRequest(settings.TelegramBotToken, settings.TelegramChatId, text, action.Id, out error);
        }
    }
}
