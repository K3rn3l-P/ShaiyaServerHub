using System;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Tool T0: elenca i comandi slash GM configurati nel canale PSM_Cmd (tabella GmCommandAllowlist).
    /// È il "doppio gate" server-side che l'AI deve conoscere PRIMA di proporre qualsiasi azione
    /// in-game: il server esegue SOLO i comandi qui presenti con Enabled=1 e Tier adeguato, a
    /// prescindere da cosa decide questo Hub — vedi PLAN §Canale comandi PSM_Cmd.
    /// </summary>
    public class GameCommandsAllowlistTool : IAiTool
    {
        private readonly HubSettings _settings;
        public string Name => "list_game_commands";
        public string Description => "Elenca i comandi slash GM configurati nel canale PSM_Cmd (Command, " +
            "Service, Tier SAFE/PLAYER/ECONOMY/SERVICE, Enabled, Description). Il server esegue SOLO i " +
            "comandi qui presenti con Enabled=1 e Tier adeguato: è il gate reale, indipendente da questo Hub.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{}}";

        public GameCommandsAllowlistTool(AiToolContext ctx) { _settings = ctx.Settings; }

        public string Execute(string argumentsJson)
        {
            string output;
            int rc = AiSqlReader.Query(_settings, "PSM_Cmd",
                "SELECT Command, Service, Tier, Enabled, Description FROM dbo.GmCommandAllowlist ORDER BY Service, Tier, Command;",
                out output);
            return rc == 0 ? output : $"[exit {rc}]\n{output}";
        }
    }
}
