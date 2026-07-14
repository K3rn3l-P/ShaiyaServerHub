using System;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>Ritmo di danno osservato — "H" nella formula del PLAN §Escalation.</summary>
    public enum Urgency { Nessuno, Lento, Veloce, Critico }

    /// <summary>Raggio d'impatto — "B" nella formula. ServerWide gestito come regola a parte (mai auto).</summary>
    public enum BlastRadius { Singolo, Gruppo, Servizio, ServerWide }

    public enum EscalationOutcome
    {
        /// <summary>Mai eseguibile (invariante violato — non dovrebbe arrivare fin qui, verificato prima).</summary>
        Deny,
        /// <summary>H critico + C≥Cmin + reversibile: agisce SUBITO, notifica dopo (danno atteso &gt; attesa).</summary>
        ImmediateContainment,
        /// <summary>Irreversibile, server-wide, o C&lt;Cmin: mette in coda e ASPETTA SEMPRE una decisione umana.</summary>
        RequireApproval,
        /// <summary>Reversibile e auto-eligibile: mette in coda con una scadenza — auto-eseguita se nessuno risponde prima.</summary>
        GraceThenAuto
    }

    public class EscalationDecision
    {
        public EscalationOutcome Outcome;
        /// <summary>Valorizzato solo per GraceThenAuto — secondi di attesa prima dell'esecuzione automatica.</summary>
        public double GraceSeconds;
        public string Reasoning;
    }

    /// <summary>
    /// Formula a punteggio per decidere se un'azione T2+ va eseguita subito, mai senza approvazione, o
    /// con una finestra di attesa dinamica — vedi PLAN §Escalation intelligente a punteggio. Valutata
    /// SOLO dopo che <see cref="Invariants"/> ha già escluso i divieti assoluti (questa classe non
    /// sostituisce quelli, li completa per la zona grigia "reversibile ma rischioso").
    /// Parametri (Cmin, pesi, grace base/min/max) da <see cref="PolicyConfigFile"/> (ai\config\policy.json,
    /// editabile senza ricompilare) — mai hardcoded qui.
    /// </summary>
    public static class EscalationEngine
    {
        public static EscalationDecision Decide(bool reversible, Urgency h, double confidence, BlastRadius b, EscalationConfig cfg = null)
        {
            cfg = cfg ?? PolicyConfigFile.LoadEscalation();
            confidence = Math.Max(0, Math.Min(1, confidence));

            // Regola 1: irreversibile O server-wide → sempre approvazione, nessuna eccezione.
            if (!reversible || b == BlastRadius.ServerWide)
            {
                return new EscalationDecision
                {
                    Outcome = EscalationOutcome.RequireApproval,
                    Reasoning = !reversible ? "Azione non reversibile: richiede sempre approvazione umana."
                                             : "Impatto server-wide: richiede sempre approvazione umana."
                };
            }

            // Regola 2: reversibile + danno critico + confidenza alta → il danno del NON agire supera
            // quello di agire subito (es. flood attivo): contenimento immediato, notifica dopo.
            if (reversible && h == Urgency.Critico && confidence >= cfg.ConfidenceMin)
            {
                return new EscalationDecision
                {
                    Outcome = EscalationOutcome.ImmediateContainment,
                    Reasoning = $"Reversibile, danno critico, confidenza {confidence:0.00}≥{cfg.ConfidenceMin:0.00}: contenimento immediato."
                };
            }

            // Regola 3: confidenza sotto soglia su un'azione comunque non-cosmetica → mai auto.
            if (confidence < cfg.ConfidenceMin)
            {
                return new EscalationDecision
                {
                    Outcome = EscalationOutcome.RequireApproval,
                    Reasoning = $"Confidenza {confidence:0.00} sotto la soglia {cfg.ConfidenceMin:0.00}: richiede approvazione."
                };
            }

            // Regola 4: auto-eligibile → grace dinamico. Più danno/raggio/confidenza ⇒ attesa più corta.
            double wH = cfg.DamageWeight.TryGetValue(UrgencyKey(h), out var w) ? w : 1;
            double uB = cfg.BlastRadiusWeight.TryGetValue(BlastRadiusKey(b), out var u) ? u : 1;
            double denom = Math.Max(0.0001, confidence * wH * uB);
            double graceMinutes = cfg.GraceBaseMinutes / denom;
            graceMinutes = Math.Max(cfg.GraceMinMinutes, Math.Min(cfg.GraceMaxMinutes, graceMinutes));

            return new EscalationDecision
            {
                Outcome = EscalationOutcome.GraceThenAuto,
                GraceSeconds = graceMinutes * 60,
                Reasoning = $"Auto-eligibile: grace {graceMinutes:0.#} min (C={confidence:0.00}, H={h}, B={b}) — " +
                            "eseguita da sola se nessuno risponde prima, annullabile/anticipabile da Telegram."
            };
        }

        private static string UrgencyKey(Urgency h) => h switch
        {
            Urgency.Nessuno => "nessuno", Urgency.Lento => "lento", Urgency.Veloce => "veloce", _ => "critico"
        };

        private static string BlastRadiusKey(BlastRadius b) => b switch
        {
            BlastRadius.Singolo => "singolo", BlastRadius.Gruppo => "gruppo", _ => "servizio"
        };
    }
}
