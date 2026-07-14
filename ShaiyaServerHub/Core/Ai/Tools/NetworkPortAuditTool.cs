using System;
using System.Linq;
using System.Text;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: audit COMPLETO della superficie di rete — incrocia le porte REALMENTE in ascolto
    /// (0.0.0.0/::) con TUTTE le regole firewall attive (non solo Shaiya-*/SSH) e classifica il
    /// rischio (OK/ATTENZIONE/RISCHIO). Wrappa FirewallManager.NetworkAudit(), lo stesso motore della
    /// scheda "Test di rete" in Funzioni→Firewall. Complementare a firewall_status (che mostra SOLO
    /// le regole gestite Shaiya-*/SSH): usa QUESTO tool per sapere davvero cosa è esposto, incluse
    /// regole non-Shaiya come Syncthing/Tailscale/SMB/RPC.
    /// </summary>
    public class NetworkPortAuditTool : IAiTool
    {
        private readonly HubSettings _settings;
        public string Name => "network_port_audit";
        public string Description => "Audit completo della rete: ogni porta REALMENTE in ascolto sul server " +
            "(0.0.0.0/::), il processo che la occupa, e se è raggiungibile da PUBBLICA/LAN/PROTETTA/ESPOSTA, " +
            "con classificazione di rischio (OK/ATTENZIONE/RISCHIO). Copre TUTTE le regole firewall attive " +
            "(non solo quelle Shaiya-*), incluse Syncthing/Tailscale/RPC/SMB e qualsiasi altra. Read-only. " +
            "Usa questo tool (non firewall_status) per sapere cosa è davvero esposto sul server.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{}}";

        public NetworkPortAuditTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                var fw = new FirewallManager();
                bool firewallOn, simplewall;
                var rows = fw.NetworkAudit(_settings?.FirewallLanCidr ?? "10.10.0.0/16", out firewallOn, out simplewall);

                var sb = new StringBuilder();
                sb.AppendLine($"Firewall attivo: {firewallOn} · simplewall filtra: {simplewall}");
                sb.AppendLine("--- Porte in ascolto (0.0.0.0/::) ---");
                foreach (var r in rows.OrderBy(x => x.Port))
                    sb.AppendLine($"{r.Port}\t{r.Process}\t{r.Purpose}\texposure={r.Exposure}\trisk={r.Risk}" +
                                  (string.IsNullOrEmpty(r.Note) ? "" : $"\tnote={r.Note}"));
                return sb.ToString();
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
