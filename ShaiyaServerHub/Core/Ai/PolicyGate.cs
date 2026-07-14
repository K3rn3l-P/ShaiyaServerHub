using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Orchestratore: unico punto da cui un tool T1+ può passare per essere eseguito — vedi PLAN
    /// §Modello di autonomia/§OBBLIGO. Due percorsi:
    /// - <see cref="Authorize"/> — T1 (auto-heal): binario autorizzato/rifiutato, esecuzione immediata
    ///   se autorizzato. Usato da tool a basso rischio, reversibili, senza bisogno di supervisione.
    /// - <see cref="Evaluate"/> — T2+ (difesa/rischiose): passa dalla formula a punteggio di
    ///   <see cref="EscalationEngine"/>, può risultare in esecuzione immediata, coda con scadenza
    ///   (auto-eseguita se nessuno risponde) o approvazione sempre manuale via Telegram.
    /// Per T0 sempre autorizzato in entrambi i percorsi (nessuna regressione sul comportamento P1).
    /// </summary>
    public enum PolicyOutcome { Denied, ExecuteImmediately, Queued }

    /// <summary>Esito di <see cref="PolicyGate.Evaluate"/> per un tool T2+.</summary>
    public class PolicyDecision
    {
        public PolicyOutcome Outcome;
        public string DenyReason;
        /// <summary>Valorizzato solo per Queued con grace (null = richiede sempre approvazione esplicita).</summary>
        public DateTime? GraceDeadlineUtc;
        public string EscalationReasoning;
    }

    public static class PolicyGate
    {
        // Rate-cap T1+: contatore in-process best-effort, stesso pattern di
        // ModelRouter.TryConsumeBudget — azzerato al riavvio dell'Hub/watchdog, non serve persistenza
        // per uno strumento pensato a scoraggiare loop anomali, non come registro di audit definitivo
        // (quello è il log hub.log, scritto comunque ad ogni decisione).
        private const int MaxActionsPerHour = 10;
        private static readonly object _lock = new object();
        private static DateTime _hourStart = DateTime.MinValue;
        private static int _countHour;

        // Circuit breaker: diverso dal rate-cap sopra (soglia soft "uso ordinario") — questo è un
        // allarme "qualcosa non va" (loop, richieste ripetute anomale, possibile manipolazione) su una
        // finestra breve. STICKY per design: una volta scattato resta aperto finché un admin non lo
        // resetta esplicitamente (bottone Impostazioni) — un circuit breaker che si auto-richiude da
        // solo col tempo non forza mai nessuno a guardare cosa è successo, perde lo scopo.
        private static readonly List<DateTime> _actionAttempts = new List<DateTime>();
        private static bool _circuitTripped;
        private static DateTime _circuitTrippedAtUtc;

        public static bool IsCircuitBreakerTripped => _circuitTripped;
        public static DateTime? CircuitBreakerTrippedAtUtc => _circuitTripped ? (DateTime?)_circuitTrippedAtUtc : null;

        /// <summary>Reset manuale (bottone Impostazioni AI) — l'unico modo per richiudere il circuito una volta aperto.</summary>
        public static void ResetCircuitBreaker()
        {
            lock (_lock) { _circuitTripped = false; _actionAttempts.Clear(); }
            App.WriteLog("AI-POLICYGATE", "Circuit breaker RESETTATO manualmente.");
        }

        /// <summary>
        /// Sliding window di TUTTI i tentativi T1+ (autorizzati o no — un loop di tentativi rifiutati è
        /// comunque un segnale di malfunzionamento). Se ne arrivano troppi in poco tempo, scatta e resta
        /// aperto finché non resettato — vedi <see cref="ResetCircuitBreaker"/>.
        /// </summary>
        private static bool CheckCircuitBreaker(HubSettings settings, out string denyReason)
        {
            denyReason = null;
            bool justTripped = false;
            lock (_lock)
            {
                if (_circuitTripped)
                {
                    denyReason = $"Circuit breaker APERTO dalle {_circuitTrippedAtUtc:yyyy-MM-dd HH:mm} UTC " +
                                 "(troppe azioni in poco tempo) — reset manuale richiesto (Impostazioni AI).";
                    return false;
                }

                var cfg = PolicyConfigFile.LoadCircuitBreaker();
                var now = DateTime.UtcNow;
                _actionAttempts.RemoveAll(t => now - t > TimeSpan.FromMinutes(cfg.WindowMinutes));
                _actionAttempts.Add(now);

                if (_actionAttempts.Count > cfg.MaxActionsInWindow)
                {
                    _circuitTripped = true;
                    _circuitTrippedAtUtc = now;
                    justTripped = true;
                    denyReason = $"Circuit breaker SCATTATO: {_actionAttempts.Count} tentativi T1+ in " +
                                 $"{cfg.WindowMinutes:0.#} minuti (soglia {cfg.MaxActionsInWindow}) — tutte le " +
                                 "azioni T1+ sospese finché non resetti manualmente (Impostazioni AI).";
                }
            }
            // Notifica FUORI dal lock: è una chiamata di rete (Telegram), non deve tenere bloccato
            // PolicyGate per altri tool nel frattempo.
            if (justTripped)
            {
                App.WriteLog("AI-POLICYGATE", "CIRCUIT BREAKER SCATTATO: " + denyReason);
                Notifier.NotifyInfo(settings, "🚨 Circuit breaker AI SCATTATO — troppe azioni in poco tempo, " +
                    "tutte le azioni T1+ sospese. Reset manuale richiesto nelle Impostazioni AI.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// True se il tool può essere eseguito; false + <paramref name="denyReason"/> altrimenti.
        /// Logga SEMPRE la decisione (autorizzato o no) in hub.log, categoria AI-POLICYGATE.
        /// </summary>
        public static bool Authorize(IAiTool tool, string argumentsJson, HubSettings settings, out string denyReason)
        {
            denyReason = null;
            if (tool == null) { denyReason = "Tool nullo."; return false; }

            if (tool.Tier == AiToolTier.T0_Observe)
                return true; // osservazione pura: nessuna governance, comportamento P1 invariato

            // ── Da qui in poi: T1+ (azioni che mutano lo stato del server) ──

            if (Invariants.KillSwitchActive)
            {
                denyReason = "Kill-switch attivo (file ai\\STOP presente) — tutte le azioni T1+ sono sospese.";
                Log(tool, false, denyReason);
                return false;
            }
            if (settings == null || !settings.AiMasterEnabled)
            {
                denyReason = "AI disattivata (interruttore principale nelle Impostazioni AI) — nessuna azione T1+ consentita.";
                Log(tool, false, denyReason);
                return false;
            }
            if (!CheckCircuitBreaker(settings, out var breakerReason))
            {
                denyReason = breakerReason;
                Log(tool, false, denyReason);
                return false;
            }
            if (!TryConsumeRateCap(out var rateReason))
            {
                denyReason = rateReason;
                Log(tool, false, denyReason);
                return false;
            }

            // Invarianti specifiche del tool — dispatch per nome. Un nuovo tool T1+ va aggiunto qui
            // esplicitamente: niente esecuzione "di default autorizzata" per un tool non ancora previsto.
            if (!CheckToolSpecificInvariants(tool, argumentsJson, settings, out var specificReason))
            {
                denyReason = specificReason;
                Log(tool, false, denyReason);
                return false;
            }

            Log(tool, true, null);
            return true;
        }

        /// <summary>
        /// Come <see cref="Authorize"/> ma per tool T2+ governati dalla formula a punteggio invece di un
        /// binario autorizzato/rifiutato — vedi <see cref="EscalationEngine"/>. Verifica PRIMA le stesse
        /// cose di Authorize (kill-switch/master-enable/rate-cap/invarianti specifiche): se una di quelle
        /// nega, la richiesta è Denied a prescindere dal punteggio, la formula non entra nemmeno in
        /// gioco. Se le passa, chiede a EscalationEngine SE e QUANDO eseguire.
        /// </summary>
        public static PolicyDecision Evaluate(IAiTool tool, string argumentsJson, HubSettings settings)
        {
            var result = new PolicyDecision();
            if (tool == null) { result.Outcome = PolicyOutcome.Denied; result.DenyReason = "Tool nullo."; return result; }

            if (Invariants.KillSwitchActive)
            {
                result.Outcome = PolicyOutcome.Denied;
                result.DenyReason = "Kill-switch attivo (file ai\\STOP presente) — tutte le azioni T1+ sono sospese.";
                Log(tool, false, result.DenyReason);
                return result;
            }
            if (settings == null || !settings.AiMasterEnabled)
            {
                result.Outcome = PolicyOutcome.Denied;
                result.DenyReason = "AI disattivata (interruttore principale nelle Impostazioni AI) — nessuna azione T1+ consentita.";
                Log(tool, false, result.DenyReason);
                return result;
            }
            if (!CheckCircuitBreaker(settings, out var breakerReason))
            {
                result.Outcome = PolicyOutcome.Denied;
                result.DenyReason = breakerReason;
                Log(tool, false, breakerReason);
                return result;
            }
            if (!TryConsumeRateCap(out var rateReason))
            {
                result.Outcome = PolicyOutcome.Denied;
                result.DenyReason = rateReason;
                Log(tool, false, rateReason);
                return result;
            }
            if (!CheckToolSpecificInvariants(tool, argumentsJson, settings, out var specificReason))
            {
                result.Outcome = PolicyOutcome.Denied;
                result.DenyReason = specificReason;
                Log(tool, false, specificReason);
                return result;
            }

            var urgency = ExtractUrgency(argumentsJson);
            var confidence = ExtractConfidence(argumentsJson);
            // I tool T2+ esistenti (kick_player, ban_player, temp_block_ip) impattano un singolo bersaglio
            // (un account o un IP) — quando arriveranno tool T2+ con raggio diverso, questo andrà estratto
            // dal tool invece che fissato qui.
            var blastRadius = BlastRadius.Singolo;

            var escalation = EscalationEngine.Decide(tool.Reversible, urgency, confidence, blastRadius);
            switch (escalation.Outcome)
            {
                case EscalationOutcome.Deny:
                    result.Outcome = PolicyOutcome.Denied;
                    result.DenyReason = escalation.Reasoning;
                    Log(tool, false, escalation.Reasoning);
                    break;
                case EscalationOutcome.ImmediateContainment:
                    result.Outcome = PolicyOutcome.ExecuteImmediately;
                    result.EscalationReasoning = escalation.Reasoning;
                    Log(tool, true, "IMMEDIATO · " + escalation.Reasoning);
                    break;
                case EscalationOutcome.RequireApproval:
                    result.Outcome = PolicyOutcome.Queued;
                    result.GraceDeadlineUtc = null;
                    result.EscalationReasoning = escalation.Reasoning;
                    Log(tool, true, "IN CODA (solo approvazione manuale) · " + escalation.Reasoning);
                    break;
                case EscalationOutcome.GraceThenAuto:
                    result.Outcome = PolicyOutcome.Queued;
                    result.GraceDeadlineUtc = DateTime.UtcNow.AddSeconds(escalation.GraceSeconds);
                    result.EscalationReasoning = escalation.Reasoning;
                    Log(tool, true, "IN CODA (grace) · " + escalation.Reasoning);
                    break;
            }
            return result;
        }

        private static Urgency ExtractUrgency(string argumentsJson)
        {
            string s = ExtractStringArg(argumentsJson, "urgency");
            return (s ?? "").ToLowerInvariant() switch
            {
                "nessuno" => Urgency.Nessuno,
                "veloce" => Urgency.Veloce,
                "critico" => Urgency.Critico,
                _ => Urgency.Lento // default prudente: né ignorato né trattato come critico
            };
        }

        private static double ExtractConfidence(string argumentsJson)
        {
            try
            {
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    if (doc.RootElement.TryGetProperty("confidence", out var el) && el.ValueKind == JsonValueKind.Number)
                        return Math.Max(0, Math.Min(1, el.GetDouble()));
                }
            }
            catch { }
            return 0.5; // default prudente: sotto la soglia tipica (Cmin=0.8) → richiede approvazione
        }

        private static bool CheckToolSpecificInvariants(IAiTool tool, string argumentsJson, HubSettings settings, out string denyReason)
        {
            switch (tool.Name)
            {
                case "restart_crashed_service":
                    string svc = ExtractStringArg(argumentsJson, "service");
                    return Invariants.CheckServiceRestart(svc, out denyReason);

                case "heal_game_services":
                    return Invariants.CheckHealAllGameServices(out denyReason);

                case "write_ai_test_script":
                    string scriptName = ExtractStringArg(argumentsJson, "name");
                    string scriptSql = ExtractStringArg(argumentsJson, "sql");
                    return Invariants.CheckWriteAiTestScript(scriptName, scriptSql, out denyReason);

                case "write_learned_note":
                    string noteTopic = ExtractStringArg(argumentsJson, "topic");
                    string noteText = ExtractStringArg(argumentsJson, "note");
                    return Invariants.CheckWriteLearnedNote(noteTopic, noteText, out denyReason);

                case "unblock_ip":
                    string unblockIp = ExtractStringArg(argumentsJson, "ip");
                    if (string.IsNullOrWhiteSpace(unblockIp) || !System.Net.IPAddress.TryParse(unblockIp, out _))
                    {
                        denyReason = "IP mancante o non valido.";
                        return false;
                    }
                    denyReason = null;
                    return true;

                case "kick_player":
                    string command = ExtractStringArg(argumentsJson, "command");
                    string target = ExtractStringArg(argumentsJson, "target");
                    if (!Invariants.CheckKickPlayer(command, target, out denyReason))
                        return false;
                    // Risolto QUI (non solo dentro Execute) per non generare una richiesta di
                    // approvazione Telegram a vuoto per un target che non esiste nemmeno nel DB —
                    // osservato nel test dal vivo di ban_player, stesso limite pre-esistente qui.
                    if (!PlayerLookup.TryResolve(settings, target, out _, out denyReason))
                        return false;
                    return true;

                case "ban_player":
                    string banTarget = ExtractStringArg(argumentsJson, "target");
                    string banReason = ExtractStringArg(argumentsJson, "reason");
                    int banDays = ExtractIntArg(argumentsJson, "days", 3650);
                    if (!Invariants.CheckBanPlayer(banTarget, banReason, banDays, out denyReason))
                        return false;
                    // Verificato PRIMA della coda/notifica Telegram (bug reale nel test dal vivo: senza
                    // questo controllo qui, credenziali mancanti venivano scoperte SOLO dentro
                    // AiBanExecutor.Execute — dopo che l'approvazione era già stata chiesta via Telegram,
                    // creando una richiesta di approvazione per un'azione che non avrebbe MAI potuto
                    // eseguire comunque).
                    if (settings == null || string.IsNullOrWhiteSpace(settings.AiBanSqlPass))
                    {
                        denyReason = "Login ban (ShaiyaAiBanExecutor) non configurato nelle Impostazioni AI — azione rifiutata prima di generare una richiesta di approvazione.";
                        return false;
                    }
                    // Stesso motivo di kick_player: mai mettere in coda un'approvazione Telegram per un
                    // target che non esiste — ancora più importante qui perché l'azione è irreversibile,
                    // sprecare un ciclo di fiducia dell'approvatore su un target fantasma è peggio.
                    if (!PlayerLookup.TryResolve(settings, banTarget, out _, out denyReason))
                        return false;
                    return true;

                case "temp_block_ip":
                    string blockIp = ExtractStringArg(argumentsJson, "ip");
                    string blockReason = ExtractStringArg(argumentsJson, "reason");
                    return Invariants.CheckTempBlockIp(blockIp, blockReason, settings?.FirewallLanCidr, out denyReason);

                case "run_scheduled_task":
                    string taskName = ExtractStringArg(argumentsJson, "task");
                    string taskReason = ExtractStringArg(argumentsJson, "reason");
                    if (!Invariants.CheckRunScheduledTask(taskName, taskReason, out denyReason))
                        return false;
                    // Esistenza verificata QUI (non solo dentro Execute) — stesso principio di
                    // PlayerLookup per kick/ban: mai generare una richiesta di approvazione Telegram
                    // per un task che non esiste nemmeno.
                    if (!TaskSchedulerService.List().Any(t => string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase)))
                    {
                        denyReason = $"Task '{taskName}' non trovato tra quelli pianificati (\\ShaiyaServerHub\\).";
                        return false;
                    }
                    return true;

                default:
                    // Tool T1+ non ancora previsto qui = rifiutato per costruzione (whitelist esplicita,
                    // stesso principio di ToolRegistry per i nomi tool: mai autorizzare "per default").
                    denyReason = $"Tool '{tool.Name}' è T1+ ma non ha invarianti specifiche registrate in " +
                                 "PolicyGate — rifiutato per sicurezza (aggiungere un case dedicato prima di usarlo).";
                    return false;
            }
        }

        private static bool TryConsumeRateCap(out string denyReason)
        {
            denyReason = null;
            lock (_lock)
            {
                var now = DateTime.Now;
                if (now - _hourStart > TimeSpan.FromHours(1)) { _hourStart = now; _countHour = 0; }
                if (_countHour >= MaxActionsPerHour)
                {
                    denyReason = $"Rate-cap azioni T1+ raggiunto ({MaxActionsPerHour}/ora) — riprova più tardi.";
                    return false;
                }
                _countHour++;
                return true;
            }
        }

        private static string ExtractStringArg(string argumentsJson, string propName)
        {
            try
            {
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    if (doc.RootElement.TryGetProperty(propName, out var el) && el.ValueKind == JsonValueKind.String)
                        return el.GetString();
                }
            }
            catch { /* argomenti malformati: il chiamante vedrà il parametro come mancante */ }
            return null;
        }

        private static int ExtractIntArg(string argumentsJson, string propName, int defaultValue)
        {
            try
            {
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    if (doc.RootElement.TryGetProperty(propName, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v))
                        return v;
                }
            }
            catch { /* argomenti malformati: il chiamante userà il default */ }
            return defaultValue;
        }

        private static void Log(IAiTool tool, bool authorized, string reason)
        {
            string verdict = authorized ? "AUTORIZZATO" : "RIFIUTATO";
            string line = $"{verdict} · {tool.Name} [{tool.Tier}]" + (reason != null ? $" · {reason}" : "");
            App.WriteLog("AI-POLICYGATE", line);
        }
    }
}
