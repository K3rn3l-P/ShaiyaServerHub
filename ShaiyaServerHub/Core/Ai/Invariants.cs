using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Regole HARD non bypassabili — vedi PLAN §Modello di autonomia e sicurezza, §OBBLIGO. Verificate
    /// da <see cref="PolicyGate"/> PRIMA di ogni tool con tier T1+. Sono la prima fetta concreta di P2:
    /// nessuna azione che muta lo stato del server esiste ancora senza passare da qui — il principio
    /// "guardrail nel codice, mai nel prompt/nella fiducia nel modello" applicato per davvero, non solo
    /// scritto nel piano. Regole statiche, deliberatamente semplici e leggibili: non è il posto per
    /// logica intelligente (quella è EscalationEngine, P2 successivo), è il posto per "mai e poi mai".
    /// </summary>
    public static class Invariants
    {
        // Whitelist servizi che i tool T1 possono riavviare — SOLO servizi di gioco, MAI infrastruttura
        // (MSSQLSERVER, sshd, PSM_AgentServer/Client): fermare quelli per errore/allucinazione sarebbe
        // uno scenario molto peggiore di un servizio di gioco fermo. Fonte: ServiceMonitor.
        // GameServiceStartOrder (unica fonte di verità sull'elenco E sull'ordine corretto — mai duplicare).
        private static readonly HashSet<string> RestartableServices =
            new HashSet<string>(ServiceMonitor.GameServiceStartOrder, StringComparer.OrdinalIgnoreCase);

        // Servizi MAI toccabili da nessun tool T1+ futuro, elencati esplicitamente (difesa in profondità
        // oltre alla whitelist sopra: anche se un domani un altro tool T1 avesse una whitelist più larga
        // per errore, questo elenco resta un secondo controllo indipendente).
        private static readonly HashSet<string> ProtectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MSSQLSERVER", "sshd", "PSM_AgentServer", "PSM_AgentClient"
        };

        /// <summary>Presenza del file ai\STOP = kill-switch attivo, tutto T1+ rifiutato immediatamente.</summary>
        public static bool KillSwitchActive => File.Exists(Path.Combine(AiPaths.Root, "STOP"));

        /// <summary>
        /// Verifica se il servizio <paramref name="serviceName"/> può essere riavviato da un tool T1.
        /// True + nessun motivo se autorizzato; false + <paramref name="denyReason"/> altrimenti.
        /// </summary>
        public static bool CheckServiceRestart(string serviceName, out string denyReason)
        {
            denyReason = null;
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                denyReason = "Nome servizio mancante.";
                return false;
            }
            if (ProtectedServices.Contains(serviceName))
            {
                denyReason = $"'{serviceName}' è un servizio protetto (infrastruttura) — mai riavviabile da un tool AI.";
                return false;
            }
            if (!RestartableServices.Contains(serviceName))
            {
                denyReason = $"'{serviceName}' non è nella whitelist dei servizi di gioco riavviabili " +
                              $"({string.Join(", ", RestartableServices)}).";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Il tool "heal_game_services" non accetta un nome servizio dal modello (nessuna superficie di
        /// iniezione: opera SEMPRE e SOLO su <see cref="ServiceMonitor.GameServiceStartOrder"/>, la
        /// stessa whitelist fissa di <see cref="CheckServiceRestart"/>) — quindi a livello di invarianti
        /// è sempre autorizzato per costruzione. Kill-switch/master-enable/rate-cap restano comunque
        /// verificati da PolicyGate PRIMA di arrivare qui, come per ogni altro tool T1+.
        /// </summary>
        public static bool CheckHealAllGameServices(out string denyReason)
        {
            denyReason = null;
            return true;
        }

        // Comandi slash consentiti al tool T2 "kick_player" — SOLO le 4 varianti di kick, già Enabled=1
        // con Tier=PLAYER lato server in GmCommandAllowlist (doppio gate: anche se questo controllo
        // avesse un bug, il server rifiuta comunque con -3 DENIED qualunque comando non in allowlist).
        private static readonly HashSet<string> KickCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "kickuid", "kickcn", "kickcid", "kickun" };

        // Target ragionevole per un identificativo/nome giocatore (osservati in produzione: "hurr1@n3",
        // "K3rn3l#", "admin1") — blocca caratteri che avrebbero senso solo come tentativo di SQL
        // injection nella stringa comando (apici, punto e virgola, spazi). Difesa in profondità: il
        // comando finisce comunque dentro N'...' passato a una stored procedure con allowlist propria,
        // non una query costruita da zero, ma un target "pulito" non deve mai poter contenere un apice.
        private static readonly Regex SafeTarget = new Regex(@"^[A-Za-z0-9@#$%_.\-]{1,24}$", RegexOptions.Compiled);

        /// <summary>
        /// Verifica se il tool T2 "kick_player" può tentare questo target. Il "command" del modello è
        /// ormai solo un HINT informativo (il comando VERO è risolto da <see cref="PlayerLookup"/>
        /// contro il database — osservato: il modello sceglieva la variante a caso, es. "kickun" con un
        /// nome-personaggio invece dello username reale, il server rifiutava silenziosamente) — quindi
        /// può mancare senza essere un rifiuto; se presente deve comunque essere una delle 4 varianti
        /// note (difesa in profondità contro un valore inventato).
        /// </summary>
        public static bool CheckKickPlayer(string command, string target, out string denyReason)
        {
            denyReason = null;
            if (!string.IsNullOrWhiteSpace(command) && !KickCommands.Contains(command))
            {
                denyReason = $"Comando '{command}' non è una delle 4 varianti di kick consentite " +
                              $"({string.Join(", ", KickCommands)}).";
                return false;
            }
            if (string.IsNullOrWhiteSpace(target) || !SafeTarget.IsMatch(target))
            {
                denyReason = "Target mancante o con caratteri non ammessi (atteso un identificativo/nome giocatore semplice).";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Verifica se il tool T3 "ban_player" può procedere (invarianti PRIMA della risoluzione target
        /// via <see cref="PlayerLookup"/> e prima di qualunque coda/notifica Telegram — un target/reason
        /// non validi non devono nemmeno generare un messaggio di approvazione). Reversibilità=false è
        /// dichiarata dal tool stesso: <see cref="EscalationEngine"/> impone SEMPRE approvazione umana
        /// per questa azione, indipendentemente da urgency/confidence — nessun controllo aggiuntivo qui
        /// serve per quello, è già strutturale.
        /// </summary>
        public static bool CheckBanPlayer(string target, string reason, int days, out string denyReason)
        {
            denyReason = null;
            if (string.IsNullOrWhiteSpace(target) || !SafeTarget.IsMatch(target))
            {
                denyReason = "Target mancante o con caratteri non ammessi (atteso un identificativo/nome giocatore semplice).";
                return false;
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                denyReason = "Motivo del ban mancante — obbligatorio per un'azione irreversibile (finisce nell'audit sp_BanUser).";
                return false;
            }
            if (days < 1 || days > 3650)
            {
                denyReason = $"Durata ban ({days} giorni) fuori range consentito (1-3650).";
                return false;
            }
            return true;
        }

        // Range IP MAI bloccabili da temp_block_ip, indipendente da qualunque segnale che il modello
        // creda di aver trovato — vedi PLAN §Invarianti HARD "Firewall": loopback e Tailscale (rete di
        // gestione admin, usata anche dal Hub stesso per SSH/RDP) sono protezione assoluta. La LAN di
        // gestione (HubSettings.FirewallLanCidr) è verificata a parte perché configurabile.
        private static readonly string[] NeverBlockCidrs = { "127.0.0.0/8", "100.64.0.0/10" };

        /// <summary>
        /// Verifica se il tool T2 "temp_block_ip" può procedere. Reason obbligatorio (finisce nel log/
        /// Telegram), IP deve essere un IPv4 valido e MAI in un range protetto (loopback/Tailscale/LAN
        /// di gestione) — un blocco sull'IP sbagliato taglierebbe fuori l'admin o la rete di gestione.
        /// </summary>
        public static bool CheckTempBlockIp(string ip, string reason, string lanCidr, out string denyReason)
        {
            denyReason = null;
            if (string.IsNullOrWhiteSpace(reason))
            {
                denyReason = "Motivo del blocco mancante — obbligatorio.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var addr) ||
                addr.AddressFamily != AddressFamily.InterNetwork)
            {
                denyReason = "IP mancante o non valido (atteso un indirizzo IPv4).";
                return false;
            }
            foreach (var cidr in NeverBlockCidrs)
            {
                if (IpInCidr(addr, cidr))
                {
                    denyReason = $"'{ip}' è nel range protetto {cidr} (loopback/Tailscale, rete di gestione admin) — mai bloccabile da un tool AI.";
                    return false;
                }
            }
            if (!string.IsNullOrWhiteSpace(lanCidr) && IpInCidr(addr, lanCidr))
            {
                denyReason = $"'{ip}' è nella LAN di gestione ({lanCidr}) — mai bloccabile da un tool AI.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Verifica se il tool T3 "run_scheduled_task" può procedere. Solo formato/presenza qui —
        /// l'ESISTENZA del task va verificata da PolicyGate (ha accesso a TaskSchedulerService.List(),
        /// non duplicata qui) PRIMA di generare una richiesta di approvazione, stesso principio già
        /// applicato a kick_player/ban_player con PlayerLookup.
        /// </summary>
        public static bool CheckRunScheduledTask(string taskName, string reason, out string denyReason)
        {
            denyReason = null;
            if (string.IsNullOrWhiteSpace(taskName))
            {
                denyReason = "Nome task mancante.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                denyReason = "Motivo mancante — obbligatorio: il contenuto di un task non è verificabile a priori (può essere qualsiasi script SQL/PS1 configurato dall'admin).";
                return false;
            }
            return true;
        }

        // Stesso charset "sicuro" del resto del progetto (nome file, niente traversal/caratteri strani).
        private static readonly Regex SafeScriptName = new Regex(@"^[A-Za-z0-9_\-]{1,60}$", RegexOptions.Compiled);

        // Stessa lista di AiSqlReader.Forbidden (duplicata qui di proposito: Invariants deve restare
        // autosufficiente e leggibile, non dipendere dai dettagli privati di un'altra classe) — uno
        // script AI-generato deve rispettare ESATTAMENTE lo stesso vincolo del canale sql_read_query,
        // dato che verrà eseguito con lo stesso login read-only.
        private static readonly Regex ForbiddenSqlKeywords = new Regex(
            @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|CREATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|sp_executesql)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Verifica se il tool T1 "write_ai_test_script" può procedere. Solo formato qui (nome file
        /// sicuro, query SELECT/WITH-only, nessuna parola chiave di scrittura) — l'esistenza del file
        /// (mai sovrascrivere) è verificata dentro Execute() perché richiede I/O su disco, stesso
        /// principio di RestartCrashedServiceTool (ri-verifica lo stato reale prima di agire).
        /// </summary>
        public static bool CheckWriteAiTestScript(string name, string sql, out string denyReason)
        {
            denyReason = null;
            if (string.IsNullOrWhiteSpace(name) || !SafeScriptName.IsMatch(name))
            {
                denyReason = "Nome script mancante o con caratteri non ammessi (solo lettere/numeri/trattini/underscore, max 60).";
                return false;
            }
            string trimmed = (sql ?? "").TrimStart();
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                denyReason = "Lo script deve essere una singola query SELECT/WITH — stesso limite del canale sql_read_query, eseguito con lo stesso login read-only.";
                return false;
            }
            if (ForbiddenSqlKeywords.IsMatch(sql ?? ""))
            {
                denyReason = "Query contiene parole chiave non permesse (scrittura/esecuzione) — solo letture pure.";
                return false;
            }
            return true;
        }

        // Stesso charset "sicuro" già usato per script name — un topic è comunque un nome file.
        private static readonly Regex SafeTopic = new Regex(@"^[A-Za-z0-9_\-]{1,60}$", RegexOptions.Compiled);
        private const int MaxNoteChars = 4000;

        /// <summary>
        /// Verifica se il tool T1 "write_learned_note" può procedere. Solo formato qui (topic sicuro,
        /// nota non vuota, cap lunghezza) — il cap dimensione FILE totale (append cumulativo nel tempo)
        /// è verificato dentro Execute() perché richiede di leggere la dimensione reale su disco.
        /// </summary>
        public static bool CheckWriteLearnedNote(string topic, string note, out string denyReason)
        {
            denyReason = null;
            if (string.IsNullOrWhiteSpace(topic) || !SafeTopic.IsMatch(topic))
            {
                denyReason = "Topic mancante o con caratteri non ammessi (solo lettere/numeri/trattini/underscore, max 60).";
                return false;
            }
            if (string.IsNullOrWhiteSpace(note))
            {
                denyReason = "Nota vuota — non ha senso scrivere una riga vuota nella knowledge base.";
                return false;
            }
            if (note.Length > MaxNoteChars)
            {
                denyReason = $"Nota troppo lunga ({note.Length} caratteri, limite {MaxNoteChars}) — riassumi.";
                return false;
            }
            return true;
        }

        private static bool IpInCidr(IPAddress address, string cidr)
        {
            var parts = (cidr ?? "").Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0].Trim(), out var baseAddr) ||
                !int.TryParse(parts[1].Trim(), out var prefixLen) || baseAddr.AddressFamily != address.AddressFamily)
                return false;

            byte[] addrBytes = address.GetAddressBytes();
            byte[] baseBytes = baseAddr.GetAddressBytes();
            int fullBytes = prefixLen / 8;
            int remBits = prefixLen % 8;

            for (int i = 0; i < fullBytes && i < addrBytes.Length; i++)
                if (addrBytes[i] != baseBytes[i]) return false;
            if (remBits > 0 && fullBytes < addrBytes.Length)
            {
                int mask = 0xFF << (8 - remBits);
                if ((addrBytes[fullBytes] & mask) != (baseBytes[fullBytes] & mask)) return false;
            }
            return true;
        }
    }
}
