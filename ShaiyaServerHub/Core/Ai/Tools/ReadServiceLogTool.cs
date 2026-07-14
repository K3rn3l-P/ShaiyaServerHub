using System;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: legge il log di sistema più recente di un servizio ps_* — colma un buco reale: prima
    /// l'AI poteva vedere SOLO lo stato RUNNING/STOPPED (services_status), mai il PERCHÉ. Un servizio
    /// Windows crea un nuovo file di log ad ogni avvio (`PS_<NOME>__system.log__<timestamp>.log`,
    /// verificato sul server reale via SSH) — questo tool trova sempre il più recente, mai un nome
    /// fisso. Uso previsto (vedi system.md): leggere PRIMA di un riavvio per capire la causa, e DOPO
    /// per confermare che sia partito senza errori — non solo "RUNNING" ma "RUNNING pulito". Logica
    /// di ricerca file condivisa con <see cref="ServiceLogReader"/> (anche usata da
    /// <see cref="KickPlayerTool"/> per verificare l'esito reale di un comando slash).
    /// </summary>
    public class ReadServiceLogTool : IAiTool
    {
        private readonly HubSettings _settings;

        public string Name => "read_service_log";
        public string Description => "Legge le ultime righe del log di sistema PIÙ RECENTE di un servizio ps_* " +
            "(nuovo file ad ogni avvio del processo, questo tool trova sempre quello più recente). Usalo PRIMA " +
            "di un riavvio per capire perché un servizio è caduto, e DOPO per confermare che sia partito pulito " +
            "(nessun errore nelle righe finali) invece di fidarti solo dello stato RUNNING. Read-only.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"service\":{\"type\":\"string\",\"description\":\"Nome servizio, es. ps_game\"}," +
            "\"lines\":{\"type\":\"integer\",\"description\":\"Numero di righe finali da leggere (default 60, max 300)\"}}," +
            "\"required\":[\"service\"]}";

        public ReadServiceLogTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string service = null;
                int lines = 60;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    if (doc.RootElement.TryGetProperty("service", out var s) && s.ValueKind == JsonValueKind.String)
                        service = s.GetString();
                    if (doc.RootElement.TryGetProperty("lines", out var l) && l.ValueKind == JsonValueKind.Number)
                        lines = Math.Max(5, Math.Min(300, l.GetInt32()));
                }
                if (string.IsNullOrWhiteSpace(service)) return "ERRORE: parametro 'service' mancante.";

                if (!ServiceLogReader.TryReadLatestTail(_settings, service, lines, out var fileName, out var content, out var error))
                    return error;

                return $"{fileName} (log del servizio '{service}')\n{content}";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
