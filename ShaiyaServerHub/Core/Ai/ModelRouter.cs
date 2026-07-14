using System;
using System.Linq;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Slot modelli + budget, letti da ai\config\models.json (modulare, editabile a mano O dalle
    /// Impostazioni AI del Hub — vedi ModelsConfigFile).
    /// - Critical: usato per incidenti reali / mitigazioni / ragionamento sui fix (default openrouter/auto,
    ///   consuma credito reale → riservato a quando serve davvero).
    /// - Default: usato per l'uso ordinario (chat, routine, controllo "buchi"). NON è "openrouter/free"
    ///   (quel router pesca A CASO tra TUTTI i modelli free, inclusi modelli nano/deboli che hanno
    ///   allucinato — es. dichiarato "motori di ricerca sospesi" quando il tool non lo aveva mai detto).
    ///   Default fisso su un modello free CONCRETO e verificato buono su tool-calling/anti-allucinazione
    ///   (dati classifica OpenRouter, luglio 2026): `tencent/hy3:free` (MoE 295B/21B attivi, "stable
    ///   tool-calling", "answers when grounded and flags when evidence is missing rather than
    ///   fabricating"). Toglie la lotteria: stessa richiesta → stesso modello, qualità prevedibile.
    /// - Fallback: usato in automatico se il modello scelto fallisce (crediti finiti, errore API, 429/5xx
    ///   persistente, o il modello pinnato viene rimosso da OpenRouter) — resta `openrouter/free` (pool
    ///   casuale) come rete di sicurezza, così l'AI non resta muta in nessun caso.
    /// </summary>
    public class AiModelSlots
    {
        public string Critical { get; set; } = "openrouter/auto";
        public string Default { get; set; } = "tencent/hy3:free";
        public string Fallback { get; set; } = "openrouter/free";
        public int MaxRequestsPerDay { get; set; } = 1000;
        public int MaxRequestsPerHour { get; set; } = 100;

        /// <summary>
        /// Se true, lo slot Default viene ri-verificato periodicamente contro il catalogo OpenRouter
        /// (vedi OpenRouterModelCatalog) e aggiornato da solo al modello free/tool-calling migliore
        /// disponibile — OpenRouter cambia spesso il roster free, un ID pinnato a mano può sparire o
        /// essere superato. Non tocca MAI Critical (credito vero, scelta deliberata dell'admin).
        /// </summary>
        public bool AutoUpdateDefault { get; set; } = true;
        /// <summary>Timestamp UTC (ISO 8601) dell'ultimo controllo auto-update riuscito. Null = mai.</summary>
        public string DefaultUpdatedAtUtc { get; set; }
        /// <summary>Motivazione leggibile dell'ultima scelta (popolarità/velocità) — mostrata in Impostazioni.</summary>
        public string DefaultUpdateNote { get; set; }
    }

    public enum AiTaskKind { Default, Critical }

    /// <summary>
    /// Sceglie il modello OpenRouter per tipo di compito (Default/Critical) e applica un budget
    /// giornaliero/orario per non sforare i limiti dell'account — vedi PLAN §OpenRouter/budget.
    /// Config in ai\config\models.json (ModelsConfigFile): editabile a mano O dalle Impostazioni AI.
    /// </summary>
    public class ModelRouter
    {
        private AiModelSlots _slots;

        // Contatori di processo: budget best-effort, azzerati al riavvio dell'Hub/watchdog.
        private static readonly object _lock = new object();
        private static System.DateTime _dayStart = System.DateTime.MinValue;
        private static System.DateTime _hourStart = System.DateTime.MinValue;
        private static int _countDay, _countHour;

        public ModelRouter() { Reload(); }

        public void Reload() => _slots = ModelsConfigFile.Load();

        /// <summary>Modello per il tipo di compito (fallback incrociato se uno slot è vuoto).</summary>
        public string ModelFor(AiTaskKind kind) => kind == AiTaskKind.Critical
            ? (string.IsNullOrWhiteSpace(_slots.Critical) ? _slots.Default : _slots.Critical)
            : (string.IsNullOrWhiteSpace(_slots.Default) ? _slots.Critical : _slots.Default);

        /// <summary>Modello di ripiego automatico se quello scelto fallisce (crediti finiti, errore API).</summary>
        public string FallbackModel() =>
            string.IsNullOrWhiteSpace(_slots.Fallback) ? _slots.Default : _slots.Fallback;

        /// <summary>
        /// Se AutoUpdateDefault è attivo e sono passate ≥24h dall'ultimo controllo, interroga il
        /// catalogo OpenRouter e aggiorna Default (miglior candidato) E Fallback (secondo miglior
        /// candidato DIVERSO da Default — rank-sum popolarità+velocità, vedi OpenRouterModelCatalog).
        /// Il Fallback NON è più la lotteria `openrouter/free`: quella pesca a caso tra TUTTI i modelli
        /// free, inclusi quelli deboli — osservato riprodurre l'identica allucinazione del vecchio
        /// Default ("motori sospesi" con risultati tool reali nel contesto) proprio quando il Default
        /// pinnato era rate-limited e scattava il fallback. `openrouter/free` resta comunque una rete di
        /// sicurezza ESTREMA: `AgentBrain` la aggiunge sempre come terzo elemento nella lista `models`
        /// (mai il primo paracadute, solo l'ultimissima risorsa se ANCHE Fallback fallisce). Sincrono:
        /// chiamare da thread di background (avvio Hub o pulsante "Verifica ora" in Impostazioni).
        /// True se ha aggiornato (o verificato ed erano già i migliori); false se non era il momento o
        /// è fallito (rete giù, API key assente...) — non solleva mai, Default/Fallback esistenti restano validi.
        /// </summary>
        public bool TryAutoUpdateDefault(HubSettings settings, bool force, out string note)
        {
            note = null;
            _slots = ModelsConfigFile.Load(); // rilegge nel caso un altro processo l'abbia già toccato
            if (!force)
            {
                if (!_slots.AutoUpdateDefault) { note = "Auto-update Default disattivato."; return false; }
                if (DateTime.TryParse(_slots.DefaultUpdatedAtUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var last) &&
                    DateTime.UtcNow - last < TimeSpan.FromHours(24))
                {
                    note = "Già verificato nelle ultime 24h.";
                    return false;
                }
            }

            if (!OpenRouterModelCatalog.FindRankedFreeToolModels(settings, out var ranked, out var err) || ranked.Count == 0)
            {
                note = "Auto-update Default fallito: " + err;
                return false;
            }

            string newDefault = ranked[0].Id;
            var runnerUp = ranked.FirstOrDefault(r => !string.Equals(r.Id, newDefault, StringComparison.OrdinalIgnoreCase));
            string newFallback = runnerUp?.Id ?? _slots.Fallback;

            bool changed = !string.Equals(_slots.Default, newDefault, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(_slots.Fallback, newFallback, StringComparison.OrdinalIgnoreCase);
            _slots.Default = newDefault;
            _slots.Fallback = newFallback;
            _slots.DefaultUpdatedAtUtc = DateTime.UtcNow.ToString("o");
            _slots.DefaultUpdateNote = ranked[0].Note + (runnerUp != null ? $" · fallback: {runnerUp.Name}" : "");
            ModelsConfigFile.Save(_slots);
            note = (changed ? "Default+Fallback aggiornati → " : "Default+Fallback confermati: ") + _slots.DefaultUpdateNote;
            return true;
        }

        /// <summary>True se c'è budget per una richiesta ora; incrementa i contatori se sì.</summary>
        public bool TryConsumeBudget(out string reason)
        {
            lock (_lock)
            {
                var now = System.DateTime.Now;
                if (now.Date != _dayStart.Date) { _dayStart = now; _countDay = 0; }
                if (now - _hourStart > System.TimeSpan.FromHours(1)) { _hourStart = now; _countHour = 0; }

                if (_countDay >= _slots.MaxRequestsPerDay)
                { reason = $"Budget giornaliero OpenRouter esaurito ({_slots.MaxRequestsPerDay}/giorno)."; return false; }
                if (_countHour >= _slots.MaxRequestsPerHour)
                { reason = $"Budget orario OpenRouter esaurito ({_slots.MaxRequestsPerHour}/ora)."; return false; }

                _countDay++; _countHour++;
                reason = null;
                return true;
            }
        }
    }
}
