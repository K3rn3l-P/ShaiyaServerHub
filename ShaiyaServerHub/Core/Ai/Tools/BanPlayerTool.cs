using System;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Primo tool T3 (rischioso/irreversibile) del Hub — vedi PLAN §Nota T3/Blocco 5. Chiama
    /// PS_UserData.dbo.sp_BanUser (stesso meccanismo di EtainShield/odbc_ban_user: motivo+durata+GM
    /// tracciati nell'audit, a differenza dell'UPDATE senza audit usato dal sito web) tramite il login
    /// dedicato least-priv ShaiyaAiBanExecutor (SOLO EXECUTE su quella procedura). Reversible=false è
    /// quello che basta a far scattare la Regola 1 di <see cref="EscalationEngine"/> ("irreversibile →
    /// sempre approvazione"): nessun altro codice deve garantire l'approvazione obbligatoria, è già
    /// strutturale nel PolicyGate/EscalationEngine esistenti — questa classe esegue SOLO dopo che una
    /// decisione di approvazione è già stata presa (come <see cref="KickPlayerTool"/>).
    /// </summary>
    public class BanPlayerTool : IAiTool
    {
        private readonly HubSettings _settings;

        public string Name => "ban_player";
        public string Description => "Banna PERMANENTEMENTE (o per N giorni) un giocatore dal server — AZIONE IRREVERSIBILE, " +
            "richiede SEMPRE approvazione umana via Telegram (mai auto-eseguita, mai una scadenza/grace, indipendentemente " +
            "da urgency/confidence). 'target' può essere il nome personaggio, lo username, il CharID o lo UserUID — il Hub " +
            "risolve da solo il UserUID interrogando il database, non serve indovinarlo. 'reason' è obbligatorio e onesto: " +
            "finisce nell'audit trail (sp_BanUser) insieme al tuo nome. 'days' opzionale (default 3650 = permanente) — " +
            "usa un valore più basso se il caso è dubbio o il ban dovrebbe essere temporaneo. Azione T3, irreversibile, " +
            "soggetta a PolicyGate + EscalationEngine.";
        public AiToolTier Tier => AiToolTier.T3_Risky;
        public bool Reversible => false;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"target\":{\"type\":\"string\",\"description\":\"Nome personaggio, username, CharID o UserUID — il Hub risolve lo UserUID da solo\"}," +
            "\"reason\":{\"type\":\"string\",\"description\":\"Perché lo stai facendo (onesto, finisce nell'audit sp_BanUser)\"}," +
            "\"days\":{\"type\":\"integer\",\"description\":\"Durata in giorni, 1-3650 (default 3650=permanente se omesso)\"}," +
            "\"urgency\":{\"type\":\"string\",\"enum\":[\"nessuno\",\"lento\",\"veloce\",\"critico\"],\"description\":\"Quanto è imminente il danno\"}," +
            "\"confidence\":{\"type\":\"number\",\"description\":\"0-1, quanto sei sicuro sia giustificato (onesto — non cambia il fatto che serva comunque approvazione)\"}}," +
            "\"required\":[\"target\",\"reason\"]}";

        public BanPlayerTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string target = null, reason = null;
                int days = 3650;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String) target = t.GetString();
                    if (root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String) reason = r.GetString();
                    if (root.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var dv) && dv > 0) days = dv;
                }

                // Ri-verifica anche qui (difesa in profondità: PolicyGate l'ha già passato prima di
                // Execute, ma un tool non deve mai fidarsi ciecamente di essere chiamato correttamente).
                if (!Invariants.CheckBanPlayer(target, reason, days, out var denyReason))
                    return "RIFIUTATO: " + denyReason;

                if (!PlayerLookup.TryResolve(_settings, target, out var player, out var lookupError))
                    return "RIFIUTATO: " + lookupError;

                int rc = AiBanExecutor.Execute(_settings, (int)player.UserUid, days, reason, out string output);
                string playerInfo = $"CharName={player.CharName}, UserID={player.UserId}, CharID={player.CharId}, UserUID={player.UserUid}";
                if (rc != 0)
                    return $"ERRORE esecuzione ban (rc={rc}, {playerInfo}): {output}";

                return $"AZIONE IRREVERSIBILE ESEGUITA: '{target}' bannato per {days} giorni ({playerInfo}) — motivo: \"{reason}\". sp_BanUser output: {output}";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
