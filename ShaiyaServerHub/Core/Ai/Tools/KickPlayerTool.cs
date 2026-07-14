using System;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Primo tool T2 (difesa) del Hub — vedi PLAN §Modello di autonomia, Blocco 2. Esegue una delle 4
    /// varianti di kick (già Enabled=1/Tier=PLAYER lato server in GmCommandAllowlist) tramite lo stesso
    /// canale usato dal Hub stesso (`usp_RunCommand`, riuso diretto del pattern in
    /// `MainWindow.AutoKickAllNormals`). A differenza di un tool T1, l'esecuzione qui NON è immediata
    /// per costruzione: passa da <see cref="PolicyGate.Evaluate"/> → <see cref="EscalationEngine"/>, che
    /// decide se agire subito, mettere in coda con una scadenza (grace) o richiedere sempre
    /// un'approvazione esplicita via Telegram — questa classe esegue SOLO quando quella decisione è già
    /// stata presa, non decide nulla da sola. La variante di comando (kickcn/kickun/kickcid/kickuid)
    /// NON è più scelta dal modello (osservato: sceglieva a caso, es. "kickun" con un nome-personaggio
    /// invece dello username reale, esito silenziosamente fallito) — è risolta dal database via
    /// <see cref="PlayerLookup"/> PRIMA di eseguire: se il target non esiste, il comando non parte affatto.
    /// </summary>
    public class KickPlayerTool : IAiTool
    {
        private readonly HubSettings _settings;

        public string Name => "kick_player";
        public string Description => "Espelle un giocatore dal server (reversibile, il player può ricollegarsi). " +
            "'target' può essere il nome personaggio, lo username, il CharID o lo UserUID — il Hub risolve da SOLO " +
            "la variante di comando corretta interrogando il database, non serve indovinarla (e se il target non " +
            "esiste il comando non viene nemmeno inviato). Richiede 'reason' onesto (perché lo stai facendo), " +
            "'urgency' (nessuno/lento/veloce/critico — quanto è imminente il danno se non agisci) e 'confidence' " +
            "(0-1 — quanto sei sicuro che sia giustificato, NON gonfiarlo per bypassare l'approvazione: una stima " +
            "onesta bassa è meglio di una falsa alta, il sistema gestisce entrambe in modo sicuro). In base a " +
            "questi valori l'azione può eseguire subito, essere pianificata con un'attesa, o richiedere " +
            "approvazione umana via Telegram — è normale, non è un errore. Azione T2 (difesa), reversibile, " +
            "soggetta a PolicyGate.";
        public AiToolTier Tier => AiToolTier.T2_Defense;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"target\":{\"type\":\"string\",\"description\":\"Nome personaggio, username, CharID o UserUID — il Hub risolve la variante di comando da solo\"}," +
            "\"reason\":{\"type\":\"string\",\"description\":\"Perché lo stai facendo (onesto, finisce nel log/Telegram)\"}," +
            "\"urgency\":{\"type\":\"string\",\"enum\":[\"nessuno\",\"lento\",\"veloce\",\"critico\"],\"description\":\"Quanto è imminente il danno\"}," +
            "\"confidence\":{\"type\":\"number\",\"description\":\"0-1, quanto sei sicuro sia giustificato (onesto)\"}}," +
            "\"required\":[\"target\",\"reason\"]}";

        public KickPlayerTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            try
            {
                string target = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                    if (doc.RootElement.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String) target = t.GetString();

                // Ri-verifica formato target anche qui (difesa in profondità: PolicyGate l'ha già
                // passato prima di arrivare a Execute, ma un tool non deve MAI fidarsi ciecamente di
                // essere stato chiamato correttamente — stesso principio di RestartCrashedServiceTool).
                if (!Invariants.CheckKickPlayer(null, target, out var denyReason))
                    return "RIFIUTATO: " + denyReason;

                // Risolve dal DB QUALE variante di comando usare — mai un'ipotesi del modello. Se il
                // target non esiste, il comando non viene nemmeno inviato al server.
                if (!PlayerLookup.TryResolve(_settings, target, out var player, out var lookupError))
                    return "RIFIUTATO: " + lookupError;

                string command = player.Command;
                string cmd = $"EXEC [PSM_Cmd].[dbo].[usp_RunCommand] @service=N'ps_game', @command=N'/{command} {target}';";
                var runner = new ScriptRunner(_settings); // auth configurata (ShaiyaTaskAgent) — MAI Windows -E, stesso pattern di AutoKickAllNormals
                int rc = runner.RunSqlQuery(cmd, out string output);
                if (rc != 0)
                    return $"Comando eseguito ma esito non OK (rc={rc}): {output}";

                // rc=0 significa SOLO che l'EXEC è andato a buon fine come istruzione T-SQL — NON che il
                // comando abbia avuto effetto lato gioco. Verifica sempre il vero esito leggendo il log
                // del servizio prima di dichiarare successo (ora comunque meno probabile: il comando è
                // risolto dal DB, non indovinato — ma un giocatore può essersi disconnesso nel frattempo).
                ServiceLogReader.TryVerifySlashCommand(_settings, "ps_game", command, target, out var verifiedOk, out var logDetail);
                string playerInfo = $"CharName={player.CharName}, UserID={player.UserId}, CharID={player.CharId}, UserUID={player.UserUid}";
                if (verifiedOk == true)
                    return $"'{target}' espulso con /{command} (risolto dal DB: {playerInfo}) — CONFERMATO dal log del server: {logDetail}";
                if (verifiedOk == false)
                    return $"RIFIUTATO DAL SERVER (non dal Hub): il comando /{command} {target} non ha avuto effetto " +
                           $"nonostante il target fosse risolto correttamente dal DB ({playerInfo}) — log: \"{logDetail}\". " +
                           "Probabile disconnessione nel frattempo o comando momentaneamente non processato.";
                return $"Comando eseguito (rc=0, {playerInfo}) ma non confermato dal log entro il tempo di attesa — " +
                       $"{logDetail} Verifica manualmente con read_service_log prima di considerarlo riuscito.";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
