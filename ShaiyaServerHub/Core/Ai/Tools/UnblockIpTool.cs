using System;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Rimozione ANTICIPATA di un blocco creato da <see cref="TempBlockIpTool"/> (prima della scadenza
    /// naturale, es. emerge che era un falso positivo) — T1 perché opera SOLO su IP realmente tracciati
    /// da <see cref="TempBlockRegistry"/> (nessuna superficie per toccare altre regole firewall), stesso
    /// principio di "undo di un'azione già reversibile" applicato a <c>heal_game_services</c>.
    /// </summary>
    public class UnblockIpTool : IAiTool
    {
        public string Name => "unblock_ip";
        public string Description => "Rimuove ANTICIPATAMENTE un blocco IP temporaneo creato da temp_block_ip (prima " +
            "della scadenza naturale) — es. se emerge che era un falso positivo (giocatore legittimo, non un attaccante). " +
            "Funziona SOLO su IP realmente bloccati da temp_block_ip in questo momento: nessun effetto su altre regole " +
            "firewall del Hub. Azione T1, sempre reversibile per costruzione (annulla un blocco già reversibile).";
        public AiToolTier Tier => AiToolTier.T1_AutoHeal;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{\"ip\":{\"type\":\"string\",\"description\":\"IP da sbloccare\"}},\"required\":[\"ip\"]}";

        public string Execute(string argumentsJson)
        {
            try
            {
                string ip = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                    if (doc.RootElement.TryGetProperty("ip", out var i) && i.ValueKind == JsonValueKind.String) ip = i.GetString();

                if (string.IsNullOrWhiteSpace(ip) || !TempBlockRegistry.TryGet(ip, out _))
                    return $"Nessun blocco temporaneo attivo trovato per '{ip}' — niente da rimuovere.";

                TempBlockRegistry.Remove(ip);
                var fw = new FirewallManager();
                string log = "";
                bool ok = fw.SyncTempBlockRule(TempBlockRegistry.ActiveIps(), msg => log += msg + "\n");
                return ok ? $"Blocco temporaneo per {ip} rimosso." : $"ERRORE risincronizzazione regola dopo rimozione {ip}: {log}";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
