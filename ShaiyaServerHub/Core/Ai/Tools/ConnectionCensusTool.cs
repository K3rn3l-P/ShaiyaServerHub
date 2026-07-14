using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using ShaiyaServerHub.Core.Ai.Sensors;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Primo sensore T0 di P3 — vedi PLAN §Fasatura P3. Wrappa <see cref="ConnectionCensus"/>: conta
    /// connessioni TCP REALMENTE stabilite per IP remoto sulle porte di gioco (30800/30810 di default),
    /// fonte primaria per rilevare flood/abuso (molte connessioni dallo stesso IP). Sola lettura, nessun
    /// impatto sul server nel leggerlo.
    /// </summary>
    public class ConnectionCensusTool : IAiTool
    {
        public string Name => "connection_census";
        public string Description => "Conta le connessioni TCP REALMENTE stabilite (Get-NetTCPConnection, stato reale " +
            "Windows) per ogni IP remoto sulle porte di gioco (default 30800/30810 — passa 'ports' per porte diverse). " +
            "Fonte primaria per rilevare flood/abuso: un giocatore normale ha poche connessioni, molte dallo stesso IP " +
            "sono un segnale concreto (non un'ipotesi). 'minCount' (default 8) evidenzia gli IP sopra soglia nella risposta. " +
            "ATTENZIONE: è una fotografia ISTANTANEA, non uno storico — un giocatore appena autenticato (sessione già " +
            "creata in GameSecuritySessions) può risultare 0 connessioni per qualche secondo finché il socket persistente " +
            "verso il mondo di gioco non si stabilisce davvero (es. mentre è ancora nella selezione personaggio). " +
            "'0 connessioni' da solo NON è un'anomalia — correla sempre con security_telemetry(focus:sessions) prima " +
            "di considerarlo un segnale.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"ports\":{\"type\":\"array\",\"items\":{\"type\":\"integer\"},\"description\":\"Porte da controllare, default [30800,30810]\"}," +
            "\"minCount\":{\"type\":\"integer\",\"description\":\"Soglia connessioni per evidenziare un IP come sospetto, default 8\"}}}";

        public string Execute(string argumentsJson)
        {
            try
            {
                int[] ports = ConnectionCensus.GamePorts;
                int minCount = 8;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("ports", out var p) && p.ValueKind == JsonValueKind.Array)
                    {
                        var list = p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number)
                                     .Select(e => e.GetInt32()).ToArray();
                        if (list.Length > 0) ports = list;
                    }
                    if (root.TryGetProperty("minCount", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mv) && mv > 0)
                        minCount = mv;
                }

                var counts = ConnectionCensus.Query(ports);
                if (counts.Count == 0)
                    return $"Nessuna connessione stabilita trovata sulle porte {string.Join(",", ports)}.";

                var sb = new StringBuilder();
                sb.AppendLine($"Connessioni stabilite per IP remoto sulle porte {string.Join(",", ports)} (soglia sospetto: {minCount}):");
                foreach (var c in counts)
                    sb.AppendLine($"{c.Ip} -> {c.Connections} connessioni" + (c.Connections >= minCount ? "  ⚠ SOPRA SOGLIA" : ""));

                int flagged = counts.Count(c => c.Connections >= minCount);
                if (flagged > 0)
                    sb.AppendLine($"\n{flagged} IP sopra soglia — possibile flood/abuso, corrobora con security_telemetry prima di agire.");

                return sb.ToString();
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
