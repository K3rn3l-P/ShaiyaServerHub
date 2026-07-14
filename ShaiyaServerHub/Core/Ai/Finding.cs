namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Gravità di un finding del digest — vedi PLAN §P4. Assegnata SEMPRE in modo deterministico dal
    /// codice (soglie fisse), mai dal modello: è quello che decide se un giro genera rumore (Low, solo
    /// log) o sveglia l'admin (Medium+, turno LLM + Telegram).
    /// </summary>
    public enum FindingSeverity { Low, Medium, High }

    /// <summary>Un singolo risultato di un controllo del digest — vedi <see cref="SecurityDigestScheduler"/>.</summary>
    public class Finding
    {
        public FindingSeverity Severity;
        public string Category;   // es. "servizi", "circuit-breaker", "sicurezza", "disco", "os-logon", "os-reboot", "sql", "web"
        public string Text;       // frase leggibile, pronta per il log/il riepilogo al modello

        public Finding(FindingSeverity severity, string category, string text)
        {
            Severity = severity;
            Category = category;
            Text = text;
        }

        public override string ToString() => $"[{Severity}] {Text}";
    }
}
