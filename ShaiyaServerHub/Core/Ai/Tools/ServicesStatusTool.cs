using System;
using System.Linq;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>Tool T0: stato dei servizi Windows del server Shaiya. Wrappa ServiceMonitor.Query() esistente.</summary>
    public class ServicesStatusTool : IAiTool
    {
        public string Name => "services_status";
        public string Description => "Stato dei servizi Windows del server Shaiya (ps_game, ps_login, ps_session, " +
            "ps_dbAgent, ps_gameLog, ps_userLog, PSM_AgentServer/Client, MSSQLSERVER): installato/in esecuzione. Read-only.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{}}";

        public ServicesStatusTool(AiToolContext ctx) { }

        public string Execute(string argumentsJson)
        {
            try
            {
                var list = ServiceMonitor.Query();
                return string.Join("\n", list.Select(s =>
                    $"{s.ServiceName} ({s.DisplayName}): " +
                    (!s.Installed ? "NOT INSTALLED" : (s.Running ? "RUNNING" : "STOPPED"))));
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
