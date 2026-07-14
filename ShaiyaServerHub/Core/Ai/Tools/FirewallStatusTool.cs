using System;
using System.Text;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>Tool T0: stato Windows Firewall + regole inbound. Wrappa FirewallManager esistente.</summary>
    public class FirewallStatusTool : IAiTool
    {
        private readonly HubSettings _settings;
        public string Name => "firewall_status";
        public string Description => "Stato dei profili Windows Firewall (Domain/Private/Public, ON/OFF, default " +
            "inbound). L'elenco regole qui sotto è FILTRATO: mostra SOLO le regole 'Shaiya-*' e SSH (la vista " +
            "gestita del pannello Firewall del Hub) — NON è la lista completa. Se una regola nota (es. " +
            "Syncthing, Tailscale) non compare qui NON significa che sia assente: usa network_port_audit per " +
            "il quadro completo (tutte le porte realmente in ascolto + tutte le regole + rischio reale).";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{}}";

        public FirewallStatusTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                var fw = new FirewallManager();
                var status = fw.GetStatus();
                var rules = fw.LoadRules(_settings?.FirewallLanCidr ?? "10.10.0.0/16");

                var sb = new StringBuilder();
                foreach (var p in status.Profiles)
                    sb.AppendLine($"Profilo {p.Name}: {(p.Enabled ? "ON" : "OFF")}, default inbound={p.DefaultInbound}");
                sb.AppendLine($"simplewall: running={status.SimplewallRunning} filtering={status.SimplewallFiltering}");
                sb.AppendLine("--- Regole inbound ---");
                foreach (var r in rules)
                    sb.AppendLine($"{r.DisplayName}: {(r.Enabled ? "ON" : "OFF")} {r.Protocol} {r.LocalPort} " +
                                  $"{r.Action} ambito={r.ScopeLabel} gestita={r.IsManaged}");
                return sb.ToString();
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
