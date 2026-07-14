using System;
using System.Text.Json;
using ShaiyaServerHub.Core.Ai.Sensors;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Secondo sensore T0 di P3 — vedi PLAN §Fasatura P3. Wrappa <see cref="SecurityTelemetry"/>:
    /// telemetria di sicurezza NATIVA del server (già scritta da sdev.dll/il sito, non simulata).
    /// Query fisse verificate una volta contro lo schema reale — niente SQL libero generato dal modello
    /// su tabelle che non conosce a menadito (stesso principio di <see cref="PlayerLookup"/>).
    /// </summary>
    public class SecurityTelemetryTool : IAiTool
    {
        private readonly HubSettings _settings;

        public string Name => "security_telemetry";
        public string Description => "Legge la telemetria di sicurezza NATIVA del server (sensori già esistenti, non " +
            "simulati): SecurityEvents (tamper/speedhack rilevati da sdev.dll), LoginAttempts (tentativi di login falliti, " +
            "brute force), GameSecuritySessions (sessioni di gioco attualmente attive). 'focus' sceglie la vista: " +
            "'summary' (default — conteggi per tipo evento + login falliti per IP + sessioni attive per IP, economico), " +
            "'events' (dettaglio ultimi eventi significativi, ESCLUSI i tipi ad altissimo volume/basso segnale come " +
            "RANGE_HACK_PVE — falsi positivi noti), 'logins' (dettaglio login falliti per IP), 'sessions' (dettaglio " +
            "sessioni attive per IP). 'minutes' = finestra temporale (default 60, max 1440).";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"focus\":{\"type\":\"string\",\"enum\":[\"summary\",\"events\",\"logins\",\"sessions\"]}," +
            "\"minutes\":{\"type\":\"integer\",\"description\":\"Finestra temporale in minuti, default 60, max 1440\"}}}";

        public SecurityTelemetryTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string focus = "summary";
                int minutes = 60;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("focus", out var f) && f.ValueKind == JsonValueKind.String) focus = f.GetString();
                    if (root.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mv)) minutes = mv;
                }
                minutes = Math.Max(5, Math.Min(1440, minutes));

                switch ((focus ?? "summary").ToLowerInvariant())
                {
                    case "events":
                        SecurityTelemetry.SignificantEvents(_settings, minutes, out var evOut);
                        return $"Eventi significativi ultimi {minutes} min (esclusi tipi ad alto volume/basso segnale):\n{evOut}";

                    case "logins":
                        SecurityTelemetry.FailedLoginsByIp(_settings, minutes, out var loginOut);
                        return $"Login falliti ultimi {minutes} min, per IP:\n{loginOut}";

                    case "sessions":
                        SecurityTelemetry.ActiveSessionsByIp(_settings, out var sessOut);
                        return $"Sessioni di gioco attualmente attive, per IP:\n{sessOut}";

                    default:
                        SecurityTelemetry.EventCountsByType(_settings, minutes, out var countOut);
                        SecurityTelemetry.FailedLoginsByIp(_settings, minutes, out var loginSummary);
                        SecurityTelemetry.ActiveSessionsByIp(_settings, out var sessSummary);
                        return $"Riepilogo telemetria sicurezza (ultimi {minutes} min):\n\n" +
                               $"== Eventi per tipo (SecurityEvents) ==\n{countOut}\n" +
                               $"== Login falliti per IP (LoginAttempts) ==\n{loginSummary}\n" +
                               $"== Sessioni attive per IP (GameSecuritySessions) ==\n{sessSummary}";
                }
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
