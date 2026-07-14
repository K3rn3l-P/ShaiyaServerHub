using System.IO;
using System.Linq;
using System.Text;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Carica i file ai\knowledge\*.md (server-facts, databases, accounts, commands, defenses, scripts)
    /// + ai\knowledge\learned\*.md (scritti dall'AI stessa nel tempo) e li assembla in un blocco di
    /// contesto per l'AgentBrain. Modulare: editabile a mano, nessuna ricompilazione — vedi PLAN
    /// §Layout modulare dei file.
    /// </summary>
    public class KnowledgeStore
    {
        private readonly string _knowledgeDir;
        private readonly string _learnedDir;

        public KnowledgeStore(string aiRoot)
        {
            _knowledgeDir = Path.Combine(aiRoot ?? "", "knowledge");
            _learnedDir = Path.Combine(_knowledgeDir, "learned");
        }

        public void EnsureFolders()
        {
            Directory.CreateDirectory(_knowledgeDir);
            Directory.CreateDirectory(_learnedDir);
        }

        /// <summary>Concatena tutti i .md di knowledge\ + knowledge\learned\ in un unico blocco (troncato oltre maxChars).</summary>
        public string LoadAll(int maxChars = 40_000)
        {
            var sb = new StringBuilder();
            try
            {
                if (Directory.Exists(_knowledgeDir))
                    foreach (var f in Directory.GetFiles(_knowledgeDir, "*.md", SearchOption.TopDirectoryOnly).OrderBy(x => x))
                        AppendFile(sb, f);

                if (Directory.Exists(_learnedDir))
                    foreach (var f in Directory.GetFiles(_learnedDir, "*.md").OrderBy(x => x))
                        AppendFile(sb, f);
            }
            catch { }

            string text = sb.ToString();
            return text.Length > maxChars ? text.Substring(0, maxChars) + "\n…[troncato]" : text;
        }

        private static void AppendFile(StringBuilder sb, string path)
        {
            try
            {
                sb.AppendLine($"### {Path.GetFileNameWithoutExtension(path)}");
                sb.AppendLine(File.ReadAllText(path));
                sb.AppendLine();
            }
            catch { }
        }
    }
}
