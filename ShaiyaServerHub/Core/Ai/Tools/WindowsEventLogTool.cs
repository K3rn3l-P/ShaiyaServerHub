using System;
using System.Text.Json;
using ShaiyaServerHub.Core.Ai.Sensors;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: Event Log Windows (login falliti OS-level, riavvii/crash inattesi, crash servizio) —
    /// vedi PLAN §P4. Query curate via <see cref="WindowsEventLog"/> (Get-WinEvent), non SQL/PowerShell
    /// libero. Fa parte del digest periodico E disponibile on-demand in chat per approfondire.
    /// </summary>
    public class WindowsEventLogTool : IAiTool
    {
        public string Name => "windows_event_log";
        public string Description => "Legge l'Event Log di Windows: 'logons' = login falliti (Security 4625/4740) " +
            "con IP remoto REALE (esclude automaticamente il rumore interno di Windows/SQL Server senza IP, " +
            "verificato su questo server), 'restarts' = riavvii/crash inattesi (System 6008/41/1001, esclude i " +
            "falsi positivi noti tipo 'Hypervisor launch failed' su VM, distingue un riavvio pianificato tramite " +
            "l'evento 1074 vicino), 'service_crashes' = crash di servizio (7031/7034/7024, distinti dal normale " +
            "cambio di stato). 'minutes' = finestra temporale (default 60, max 1440).";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"focus\":{\"type\":\"string\",\"enum\":[\"logons\",\"restarts\",\"service_crashes\"]}," +
            "\"minutes\":{\"type\":\"integer\",\"description\":\"Finestra temporale in minuti, default 60, max 1440\"}}," +
            "\"required\":[\"focus\"]}";

        public string Execute(string argumentsJson)
        {
            try
            {
                string focus = null;
                int minutes = 60;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("focus", out var f) && f.ValueKind == JsonValueKind.String) focus = f.GetString();
                    if (root.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mv)) minutes = mv;
                }
                minutes = Math.Max(5, Math.Min(1440, minutes));

                switch ((focus ?? "").ToLowerInvariant())
                {
                    case "logons":
                        WindowsEventLog.FailedLogons(minutes, out var logonOut);
                        return $"Login falliti OS con IP remoto reale, ultimi {minutes} min:\n{logonOut}";
                    case "restarts":
                        WindowsEventLog.UnexpectedRestarts(minutes, out var restartOut);
                        return $"Riavvii/crash inattesi, ultimi {minutes} min:\n{restartOut}";
                    case "service_crashes":
                        WindowsEventLog.ServiceCrashes(minutes, out var crashOut);
                        return $"Crash di servizio, ultimi {minutes} min:\n{crashOut}";
                    default:
                        return "RIFIUTATO: 'focus' mancante o non valido (logons/restarts/service_crashes).";
                }
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
