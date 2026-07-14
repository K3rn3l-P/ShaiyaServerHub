using System;
using ShaiyaServerHub.Core.WebServer;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>Tool T0: salute/certificato del sito web. Wrappa WebProbe.Probe() esistente.</summary>
    public class WebHealthTool : IAiTool
    {
        private readonly HubSettings _settings;
        public string Name => "web_health";
        public string Description => "Sonda salute/certificato del sito web (Apache/XAMPP): raggiungibilità, " +
            "stato HTTP, worker Apache, scadenza certificato TLS. Read-only.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{}}";

        public WebHealthTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                var ws = WebServerFactory.Create(_settings);
                var r = WebProbe.Probe(ws);
                return r.Summary();
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
