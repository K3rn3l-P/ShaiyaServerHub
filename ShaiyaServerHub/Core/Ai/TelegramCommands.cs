using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Comandi slash del bot Telegram — scorciatoie per non dover ricordare/scrivere a mano quale tool
    /// usare per ogni area del server (richiesta esplicita: "non posso ricordarmi tutti i controlli").
    /// Ogni comando espande a un PROMPT precompilato che passa dalla STESSA pipeline
    /// <see cref="AgentBrain.Chat"/> di qualunque messaggio libero — stesso <see cref="PolicyGate"/>,
    /// stessa corroborazione/ragionamento: è una scorciatoia di formulazione, NON un bypass che esegue
    /// un tool direttamente. Nomi comando vincolati dalla Bot API Telegram: 1-32 caratteri, solo
    /// <c>[a-z0-9_]</c> (niente trattini, verificato via ricerca — "db-audit" verrebbe rifiutato da
    /// setMyCommands, per questo sotto sono tutti con underscore).
    /// </summary>
    public static class TelegramCommands
    {
        public class Entry
        {
            public string Command;       // senza "/"
            public string Description;   // mostrata nell'autocomplete Telegram, poche parole
            public string Prompt;        // testo inoltrato ad AgentBrain.Chat
        }

        public static readonly List<Entry> All = new List<Entry>
        {
            new Entry
            {
                Command = "db_audit",
                Description = "Sicurezza database: permessi, schema-drift, login SQL falliti",
                Prompt = "Fai un controllo di sicurezza del database: usa sql_audit (focus summary) per " +
                    "modifiche di schema recenti e login SQL falliti a livello server, poi " +
                    "testsuite_status(detail:true) per i controlli SQL in WARN/FAIL più recenti (sysadmin " +
                    "fuori whitelist, TRUSTWORTHY/db_chaining, assembly CLR, permessi PUBLIC, account sa). " +
                    "Dammi un riepilogo chiaro di eventuali problemi, corrobora prima di allarmare."
            },
            new Entry
            {
                Command = "web_audit",
                Description = "Sicurezza sito web: esposizione, TLS, pentest",
                Prompt = "Controlla la sicurezza del sito web: usa web_health per raggiungibilità/certificato " +
                    "TLS, poi testsuite_status(detail:true) per i controlli in WARN/FAIL relativi a " +
                    "esposizione web/TLS/pentest (security-audit-advanced, security-web-audit, web_exposure). " +
                    "Riassumi i problemi trovati, se qualcuno è WARN da tempo dillo chiaramente."
            },
            new Entry
            {
                Command = "autostart",
                Description = "Audit persistenza Windows (autostart/config-drift)",
                Prompt = "Esegui autoruns_audit e dimmi se ci sono voci NUOVE rispetto al baseline salvato " +
                    "(persistenza sospetta) — se il tool dice che non è scaricato, dimmelo chiaramente."
            },
            new Entry
            {
                Command = "services",
                Description = "Stato servizi di gioco e salute SQL Server",
                Prompt = "Controlla services_status per i servizi di gioco e sql_health per la salute di " +
                    "SQL Server (blocking/tempdb/query lunghe) — segnala eventuali problemi, se un servizio " +
                    "di gioco è fermo dimmelo esplicitamente (non serve che tu lo riavvii senza che te lo chieda)."
            },
            new Entry
            {
                Command = "network",
                Description = "Firewall e porte esposte sulla rete",
                Prompt = "Controlla network_port_audit (fonte autorevole per l'esposizione reale) e " +
                    "firewall_status, dimmi se qualcosa risulta esposto in modo inatteso rispetto al normale " +
                    "(gioco 30800/30810, web 80/443, SSH/RDP LAN)."
            },
            new Entry
            {
                Command = "security",
                Description = "Eventi sicurezza gioco, tamper, login OS falliti",
                Prompt = "Controlla security_telemetry (focus summary) per eventi di sicurezza/tamper e login " +
                    "di gioco falliti, e windows_event_log(focus:\"logons\") per login OS falliti — segnala " +
                    "pattern sospetti SOLO se corroborati da più sensori, mai da un singolo evento isolato."
            },
            new Entry
            {
                Command = "tasks",
                Description = "Task pianificati falliti o in errore",
                Prompt = "Controlla scheduled_tasks_status ed elenca eventuali task in errore o disabilitati " +
                    "inaspettatamente — se ne trovi uno in errore, leggine il log completo prima di proporre un rilancio."
            },
            new Entry
            {
                Command = "disk",
                Description = "Spazio disco disponibile",
                Prompt = "Controlla disk_space e segnala se qualche volume è sotto una soglia di sicurezza " +
                    "(indicativamente <10% libero)."
            },
            new Entry
            {
                Command = "test_suite",
                Description = "Riassunto ultimo esito Test Suite completo",
                Prompt = "Riassumi lo stato dell'ultimo run della Test Suite con testsuite_status(detail:true) " +
                    "— elenca i controlli in WARN/FAIL/ERRORE con una riga di dettaglio ciascuno. Se vuoi " +
                    "approfondire un controllo specifico usa testsuite_status(script:\"<nome>\")."
            }
        };

        public static bool TryGet(string command, out Entry entry)
        {
            command = (command ?? "").TrimStart('/').ToLowerInvariant();
            entry = All.FirstOrDefault(e => e.Command == command);
            return entry != null;
        }

        /// <summary>Testo statico per /help — MAI generato dal modello, sempre disponibile anche a OpenRouter giù.</summary>
        public static string HelpText()
        {
            var sb = new StringBuilder("**Comandi disponibili**\n\n");
            foreach (var e in All)
                sb.Append("/").Append(e.Command).Append(" — ").Append(e.Description).Append('\n');
            sb.Append("\nPuoi anche scrivere in linguaggio naturale, senza comando.");
            return sb.ToString();
        }
    }
}
