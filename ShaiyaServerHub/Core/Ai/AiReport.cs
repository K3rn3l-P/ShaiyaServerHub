namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Stato sintetico del modulo AI per la card Dashboard (pattern MaintenanceReport/KeyRotationReport):
    /// non fa chiamate di rete, solo verifica cosa è configurato. "Salute AI" vera (ultimo alert,
    /// pending) arriva in P2 con ActionQueue/OutcomeTracker.
    /// </summary>
    public class AiReport
    {
        public bool MasterEnabled;
        public bool ApiKeyConfigured;
        public bool SqlReaderConfigured;
        public bool TelegramConfigured;
        public int ToolCount;

        public static AiReport Load(HubSettings settings)
        {
            var r = new AiReport();
            try
            {
                r.MasterEnabled = settings.AiMasterEnabled;
                r.ApiKeyConfigured = !string.IsNullOrWhiteSpace(settings.OpenRouterApiKey);
                r.SqlReaderConfigured = !string.IsNullOrWhiteSpace(settings.AiSqlReadPass);
                r.TelegramConfigured = !string.IsNullOrWhiteSpace(settings.TelegramBotToken) &&
                                        !string.IsNullOrWhiteSpace(settings.TelegramChatId);
                var tools = new ToolRegistry(new AiToolContext { Settings = settings });
                r.ToolCount = tools.All.Count;
            }
            catch { }
            return r;
        }
    }
}
