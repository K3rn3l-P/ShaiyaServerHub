using System;
using System.IO;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Percorsi della cartella "ai\" modulare (accanto all'exe, come le altre aree del Hub):
    /// config (models.json/policy.json), instructions (system.md/rules.md/playbooks), knowledge
    /// (server-facts/databases/accounts/commands/defenses/scripts + learned\), scripts (AI-generati),
    /// sessions (log chat/azioni). Vedi PLAN §Layout modulare dei file.
    /// </summary>
    public static class AiPaths
    {
        public static string Root => Path.Combine(AppContext.BaseDirectory, "ai");
        public static string ConfigDir => Path.Combine(Root, "config");
        public static string InstructionsDir => Path.Combine(Root, "instructions");
        public static string PlaybooksDir => Path.Combine(InstructionsDir, "playbooks");
        public static string KnowledgeDir => Path.Combine(Root, "knowledge");
        public static string LearnedDir => Path.Combine(KnowledgeDir, "learned");
        public static string ScriptsSqlDir => Path.Combine(Root, "scripts", "sql");
        public static string ScriptsPs1Dir => Path.Combine(Root, "scripts", "ps1");
        public static string SessionsDir => Path.Combine(Root, "sessions");

        public static void EnsureFolders()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                Directory.CreateDirectory(PlaybooksDir);
                Directory.CreateDirectory(LearnedDir);
                Directory.CreateDirectory(ScriptsSqlDir);
                Directory.CreateDirectory(ScriptsPs1Dir);
                Directory.CreateDirectory(SessionsDir);
            }
            catch { }
        }
    }
}
