using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>Sezione "escalation" di ai\config\policy.json — vedi PLAN §Escalation intelligente a punteggio.</summary>
    public class EscalationConfig
    {
        public double ConfidenceMin { get; set; } = 0.8;
        public double GraceBaseMinutes { get; set; } = 30;
        public double GraceMinMinutes { get; set; } = 0;
        public double GraceMaxMinutes { get; set; } = 60;
        public Dictionary<string, double> DamageWeight { get; set; } = new Dictionary<string, double>
        {
            ["nessuno"] = 0.25, ["lento"] = 1, ["veloce"] = 4, ["critico"] = 16
        };
        public Dictionary<string, double> BlastRadiusWeight { get; set; } = new Dictionary<string, double>
        {
            ["singolo"] = 1, ["gruppo"] = 1.5, ["servizio"] = 3
            // "serverWide" non è un peso numerico: gestito come regola a parte (sempre approvazione).
        };
    }

    /// <summary>Sezione "circuitBreaker" di ai\config\policy.json — vedi PolicyGate.CheckCircuitBreaker.</summary>
    public class CircuitBreakerConfig
    {
        public int MaxActionsInWindow { get; set; } = 8;
        public double WindowMinutes { get; set; } = 15;
    }

    /// <summary>
    /// Load di ai\config\policy.json — scaffoldato in P1 ("non ancora letto dal codice"), da questo
    /// blocco P2 in poi è la fonte reale dei parametri di <see cref="EscalationEngine"/> e del circuit
    /// breaker in <see cref="PolicyGate"/>. Solo lettura (nessuna UI di modifica ancora — editabile a
    /// mano nel file, coerente col resto del modulo AI). L'enforcement degli invarianti HARD resta
    /// sempre in Invariants.cs, mai in questo file: un valore sbagliato qui può al più cambiare UN
    /// grace period o la sensibilità del circuit breaker, mai bypassare un divieto assoluto.
    /// </summary>
    public static class PolicyConfigFile
    {
        private static string Path_ => Path.Combine(AiPaths.ConfigDir, "policy.json");

        public static CircuitBreakerConfig LoadCircuitBreaker()
        {
            try
            {
                if (File.Exists(Path_))
                {
                    using (var doc = JsonDocument.Parse(File.ReadAllText(Path_)))
                    {
                        if (doc.RootElement.TryGetProperty("circuitBreaker", out var cb))
                        {
                            var cfg = new CircuitBreakerConfig();
                            if (cb.TryGetProperty("maxActionsInWindow", out var m) && m.ValueKind == JsonValueKind.Number)
                                cfg.MaxActionsInWindow = m.GetInt32();
                            if (cb.TryGetProperty("windowMinutes", out var w) && w.ValueKind == JsonValueKind.Number)
                                cfg.WindowMinutes = w.GetDouble();
                            return cfg;
                        }
                    }
                }
            }
            catch { }
            return new CircuitBreakerConfig();
        }

        public static EscalationConfig LoadEscalation()
        {
            try
            {
                if (File.Exists(Path_))
                {
                    using (var doc = JsonDocument.Parse(File.ReadAllText(Path_)))
                    {
                        if (doc.RootElement.TryGetProperty("escalation", out var esc))
                        {
                            var cfg = new EscalationConfig();
                            if (esc.TryGetProperty("confidenceMin", out var cm)) cfg.ConfidenceMin = cm.GetDouble();
                            if (esc.TryGetProperty("graceBaseMinutes", out var gb)) cfg.GraceBaseMinutes = gb.GetDouble();
                            if (esc.TryGetProperty("graceMinMinutes", out var gmin)) cfg.GraceMinMinutes = gmin.GetDouble();
                            if (esc.TryGetProperty("graceMaxMinutes", out var gmax)) cfg.GraceMaxMinutes = gmax.GetDouble();
                            if (esc.TryGetProperty("damageWeight", out var dw))
                                foreach (var p in dw.EnumerateObject())
                                    if (p.Value.ValueKind == JsonValueKind.Number) cfg.DamageWeight[p.Name] = p.Value.GetDouble();
                            if (esc.TryGetProperty("blastRadiusWeight", out var bw))
                                foreach (var p in bw.EnumerateObject())
                                    if (p.Value.ValueKind == JsonValueKind.Number) cfg.BlastRadiusWeight[p.Name] = p.Value.GetDouble();
                            return cfg;
                        }
                    }
                }
            }
            catch { /* config assente/corrotta → default sicuri (già nei property initializer) */ }
            return new EscalationConfig();
        }
    }
}
