using System;
using System.Linq;
using System.Threading;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Sweep proattivo — vedi PLAN §Componenti codice/§Fasatura P2. Colma il gap principale rimasto
    /// dopo i Blocchi 1/2: fino ad ora l'AI agiva SOLO se qualcuno le scriveva in chat — se un servizio
    /// cadeva mentre l'admin era via, restava giù finché non se ne accorgeva qualcuno. Ogni ~3 minuti:
    /// pre-check ECONOMICO in C# puro (nessuna chiamata LLM, nessun costo) via
    /// <see cref="ServiceMonitor.Query"/> — solo se trova davvero un servizio di gioco fermo invoca
    /// <see cref="AgentBrain.Chat"/> con un prompt sintetico, riusando la STESSA pipeline già gated da
    /// <see cref="PolicyGate"/>/<see cref="Invariants"/> di un turno chat normale — nessun nuovo percorso
    /// di esecuzione, nessun nuovo guardrail da scrivere.
    /// Deliberatamente NEL PROCESSO Hub (avviato da <c>AiControl.Init</c>, come <see cref="ApprovalWatchdog"/>)
    /// invece di un CLI headless separato (<c>--ai-watch</c>, ancora non costruito): un secondo processo
    /// avrebbe un `PolicyGate`/`ActionQueue` INDIPENDENTE (stato solo in-process) — rischio concreto di
    /// doppie esecuzioni se sia l'Hub aperto sia un watcher headless girassero insieme. Copre "l'admin
    /// non sta guardando la chat ma l'Hub è acceso sul server" (il caso normale, vedi CLAUDE.md "Hub gira
    /// sul server") — non ancora "il processo Hub stesso non gira": quello resta un affinamento futuro.
    /// </summary>
    public class WatchLoop : IDisposable
    {
        private readonly HubSettings _settings;
        /// <summary>Non readonly: se le Impostazioni cambiano, AiControl ricrea AgentBrain e aggiorna questo riferimento.</summary>
        public AgentBrain Brain;

        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(3);
        // Se lo STESSO insieme di servizi resta fermo, non ri-invocare il modello ad ogni giro (spreco
        // di budget/richieste) — solo se cambia l'insieme, oppure è passato abbastanza tempo dall'ultimo
        // tentativo (nel caso il tentativo precedente non abbia risolto, es. si è fermato al checkpoint
        // senza nessuno che confermasse: il giro successivo ci riprova da capo, nessun blocco permanente).
        private static readonly TimeSpan MinCooldownBetweenTriggers = TimeSpan.FromMinutes(5);

        private readonly Timer _timer;
        private volatile bool _busy;
        private DateTime _lastTriggerUtc = DateTime.MinValue;
        private string _lastDownSetKey = "";

        public WatchLoop(HubSettings settings, AgentBrain brain)
        {
            _settings = settings;
            Brain = brain;
            _timer = new Timer(Tick, null, SweepInterval, SweepInterval);
        }

        private void Tick(object state)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (!_settings.AiMasterEnabled || Invariants.KillSwitchActive || Brain == null) return;

                var down = ServiceMonitor.Query()
                    .Where(s => s.Installed && !s.Running &&
                                ServiceMonitor.GameServiceStartOrder.Contains(s.ServiceName, StringComparer.OrdinalIgnoreCase))
                    .Select(s => s.ServiceName)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (down.Count == 0) { _lastDownSetKey = ""; return; }

                string downKey = string.Join(",", down);
                bool sameAsLast = downKey == _lastDownSetKey;
                bool cooldownElapsed = DateTime.UtcNow - _lastTriggerUtc >= MinCooldownBetweenTriggers;
                if (sameAsLast && !cooldownElapsed) return;

                _lastDownSetKey = downKey;
                _lastTriggerUtc = DateTime.UtcNow;

                App.WriteLog("AI-WATCHLOOP", $"Sweep: servizi di gioco fermi rilevati ({downKey}) — avvio turno auto-heal.");
                Notifier.NotifyInfo(_settings, $"🔍 Sweep automatico: servizi di gioco fermi rilevati ({downKey}) — l'AI sta intervenendo.");

                string prompt = $"[Sweep automatico, nessun admin presente] Rilevati servizi di gioco fermi: {downKey}. " +
                                 "Controlla la causa (log) e sistema quello che non va.";
                var result = Brain.Chat(prompt, null, AiTaskKind.Default,
                    msg => App.WriteLog("AI-WATCHLOOP", msg), CancellationToken.None);

                string outcome = result.Cancelled ? "(annullato)" : result.Ok ? result.Answer : ("ERRORE: " + result.Error);
                App.WriteLog("AI-WATCHLOOP", "Esito turno: " + outcome);
                // Livello "turno sweep" — non duplica i Record per-tool già fatti dentro AgentBrain
                // (es. "restart_crashed_service"): questo traccia se il TURNO complessivo ha concluso
                // qualcosa o si è fermato a metà (es. al checkpoint, vedi limite noto nel piano).
                OutcomeTracker.Record("watchloop_sweep", result.Ok ? "Executed" : "Failed", $"{downKey}: {Truncate(outcome, 200)}");
                // Limite reale Telegram sendMessage = 4096 caratteri: 500 (usato al primo giro) era un
                // taglio arbitrario troppo aggressivo, ha troncato un riepilogo a metà frase — osservato
                // nel test dal vivo. Margine sotto 4096 per prefisso+eventuale nota di troncamento.
                Notifier.NotifyInfo(_settings, $"🔍 Sweep automatico completato:\n{Truncate(outcome, 3800)}");
            }
            catch (Exception ex) { App.WriteLog("AI-WATCHLOOP", "errore tick: " + ex.Message); }
            finally { _busy = false; }
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";

        public void Dispose() => _timer?.Dispose();
    }
}
