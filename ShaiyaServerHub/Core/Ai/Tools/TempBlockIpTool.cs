using System;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Primo tool T2 di contenimento IP-level di P3 (colma il gap segnalato dai playbook flood-abuso/
    /// bruteforce-login: prima esistevano solo kick_player/ban_player, che agiscono su un ACCOUNT già
    /// autenticato, non su un IP che non ha ancora un account riconducibile). Reversibile per
    /// costruzione (scade sempre, mai permanente) — vedi <see cref="TempBlockScheduler"/>.
    /// UNA SOLA regola firewall condivisa (<see cref="FirewallManager.SyncTempBlockRule"/>), mai una
    /// regola per IP: centinaia di regole individuali degraderebbero New-NetFirewallRule/l'enumerazione
    /// PowerShell (limite noto del layer di management, non del filtro WFP) — pattern verificato via
    /// ricerca web (Microsoft Learn + esempi community: -RemoteAddress con lista/array in un'unica
    /// regola è lo standard). Il rate-cap condiviso di PolicyGate (10 azioni T1+/ora) limita comunque
    /// quanti NUOVI IP l'AI può bloccare in un'ora — un vero DDoS volumetrico da centinaia di IP resta
    /// fuori scope per questo Hub (serve protezione a monte, non un pannello admin).
    /// </summary>
    public class TempBlockIpTool : IAiTool
    {
        private readonly HubSettings _settings;

        public string Name => "temp_block_ip";
        public string Description => "Blocca TEMPORANEAMENTE un IP (tutte le porte TCP in ingresso) via una regola " +
            "firewall dedicata, rimossa AUTOMATICAMENTE allo scadere di 'minutes' (default 30, range 5-360) — mai " +
            "permanente, sempre reversibile. RIFIUTA per costruzione IP nella LAN di gestione, Tailscale (rete admin) " +
            "o loopback: quegli IP non sono MAI bloccabili, indipendentemente da cosa suggerisce la telemetria. Usa " +
            "questo SOLO per flood/abuso IP-level SENZA un account riconducibile (vedi playbook flood-abuso.md) — se " +
            "riesci a identificare l'account, kick_player/ban_player sono la scelta migliore (più mirati, meno " +
            "collaterali su eventuali altri utenti dietro lo stesso NAT). Azione T2 (difesa), reversibile, soggetta a " +
            "PolicyGate.";
        public AiToolTier Tier => AiToolTier.T2_Defense;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"ip\":{\"type\":\"string\",\"description\":\"Indirizzo IPv4 da bloccare\"}," +
            "\"reason\":{\"type\":\"string\",\"description\":\"Perché lo stai facendo (onesto, finisce nel log/Telegram)\"}," +
            "\"minutes\":{\"type\":\"integer\",\"description\":\"Durata del blocco in minuti, default 30, range 5-360\"}," +
            "\"urgency\":{\"type\":\"string\",\"enum\":[\"nessuno\",\"lento\",\"veloce\",\"critico\"]}," +
            "\"confidence\":{\"type\":\"number\",\"description\":\"0-1, quanto sei sicuro sia giustificato\"}}," +
            "\"required\":[\"ip\",\"reason\"]}";

        public TempBlockIpTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string ip = null, reason = null;
                int minutes = 30;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("ip", out var i) && i.ValueKind == JsonValueKind.String) ip = i.GetString();
                    if (root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String) reason = r.GetString();
                    if (root.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mv) && mv > 0) minutes = mv;
                }
                minutes = Math.Max(5, Math.Min(360, minutes));

                // Ri-verifica anche qui (difesa in profondità: PolicyGate l'ha già passato prima di
                // Execute, ma un tool non deve mai fidarsi ciecamente di essere chiamato correttamente).
                if (!Invariants.CheckTempBlockIp(ip, reason, _settings?.FirewallLanCidr, out var denyReason))
                    return "RIFIUTATO: " + denyReason;

                var expires = DateTime.UtcNow.AddMinutes(minutes);
                TempBlockRegistry.Add(ip, expires, reason);

                var fw = new FirewallManager();
                string log = "";
                bool ok = fw.SyncTempBlockRule(TempBlockRegistry.ActiveIps(), msg => log += msg + "\n");
                if (!ok)
                {
                    TempBlockRegistry.Remove(ip); // rollback dello stato in-memory se la regola reale non è stata applicata
                    return $"ERRORE creazione/aggiornamento regola firewall per {ip}: {log}";
                }

                return $"IP {ip} bloccato temporaneamente (tutte le porte TCP in ingresso) fino a " +
                       $"{expires:yyyy-MM-dd HH:mm} UTC ({minutes} minuti) — motivo: \"{reason}\". " +
                       "Rimozione automatica allo scadere, oppure usa unblock_ip prima se emerge che era un falso positivo.";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
