using System.Collections.Generic;

namespace ShaiyaServerHub.Core
{
    /// <summary>Stato di un profilo di Windows Firewall (Domain/Private/Public).</summary>
    public class FirewallProfileState
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public string DefaultInbound { get; set; }   // Allow | Block | NotConfigured
    }

    /// <summary>Fotografia dello stato firewall + motori terzi.</summary>
    public class FirewallStatus
    {
        public List<FirewallProfileState> Profiles { get; set; } = new List<FirewallProfileState>();
        public bool SimplewallRunning { get; set; }    // processo presente (tray)
        public bool SimplewallFiltering { get; set; }  // ha filtri WFP attivi = sta davvero filtrando

        /// <summary>Almeno un profilo attivo = il firewall di Windows sta filtrando.</summary>
        public bool AnyEnabled => Profiles.Exists(p => p.Enabled);
        public bool AllEnabled => Profiles.Count > 0 && Profiles.TrueForAll(p => p.Enabled);
    }

    /// <summary>Riga regola firewall (inbound) mostrata nella griglia.</summary>
    public class FirewallRuleRow : Notify
    {
        public string DisplayName { get; set; }
        public bool Enabled { get; set; }
        public string Protocol { get; set; }        // TCP | UDP | Any
        public string LocalPort { get; set; }       // "30800,30810" | "Any"
        public string RemoteAddress { get; set; }   // "Any" | "10.10.0.0/16" | ...
        public string Action { get; set; }          // Allow | Block

        /// <summary>Ambito leggibile: Pubblico (Any) o Solo LAN / ristretto.</summary>
        public string ScopeLabel { get; set; }
        /// <summary>True se gestibile/eliminabile dal pannello (regole Shaiya-*).</summary>
        public bool IsManaged { get; set; }

        // Esito dell'ultimo "Test di rete" per le porte di questa regola.
        private string _health = "UNKNOWN";   // OK | WARN | RISK | UNKNOWN
        public string Health { get => _health; set { if (Set(ref _health, value)) Raise(nameof(HealthTip)); } }

        private string _healthTip;
        public string HealthTip { get => _healthTip; set => Set(ref _healthTip, value); }
    }

    /// <summary>Esito dell'audit di una porta in ascolto su 0.0.0.0/::.</summary>
    public class PortAuditRow
    {
        public int Port { get; set; }
        public string Process { get; set; }
        public string Purpose { get; set; }         // descrizione nota (gioco/web/gestione/infra)
        public string Exposure { get; set; }        // PUBBLICA | LAN | LOCALE | ESPOSTA
        public string Risk { get; set; }            // OK | ATTENZIONE | RISCHIO
        public string Note { get; set; }
    }
}
