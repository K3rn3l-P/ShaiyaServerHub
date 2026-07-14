using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Scopre tutte le <see cref="IAiTool"/> dell'assembly via reflection e le espone all'AgentBrain.
    /// Whitelist per costruzione: SOLO i tipi trovati qui possono essere chiamati dal modello — un nome
    /// storpiato/inventato dall'LLM semplicemente non esiste nel dizionario → rifiutato (vedi invariante
    /// "tool sconosciuto = rifiutato" nel PLAN). Questo è il meccanismo di estensibilità: una nuova vista
    /// del Hub che vuole essere "vista" dall'AI aggiunge una classe IAiTool, nessun'altra modifica.
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, IAiTool> _tools = new Dictionary<string, IAiTool>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<IAiTool> All => _tools.Values;

        public ToolRegistry(AiToolContext ctx)
        {
            var toolType = typeof(IAiTool);
            IEnumerable<Type> types;
            try
            {
                types = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => toolType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null && toolType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
            }

            foreach (var t in types)
            {
                try
                {
                    var ctor = t.GetConstructor(new[] { typeof(AiToolContext) });
                    var instance = ctor != null
                        ? (IAiTool)ctor.Invoke(new object[] { ctx })
                        : (IAiTool)Activator.CreateInstance(t);
                    if (instance == null || string.IsNullOrWhiteSpace(instance.Name)) continue;
                    _tools[instance.Name] = instance;
                }
                catch (Exception ex) { App.WriteLog("AI-TOOLREG", $"tool {t.Name} non caricato: {ex.Message}"); }
            }
        }

        public bool TryGet(string name, out IAiTool tool) => _tools.TryGetValue(name ?? "", out tool);

        /// <summary>Catalogo capabilities leggibile, iniettato nel system prompt.</summary>
        public string CapabilitiesCatalog()
        {
            var sb = new StringBuilder();
            foreach (var t in _tools.Values.OrderBy(x => x.Tier).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- {t.Name} [{t.Tier}, {(t.Reversible ? "reversibile" : "non-reversibile")}]: {t.Description}");
            return sb.ToString();
        }

        /// <summary>Schema tool-calling (stile OpenAI "tools") per l'OpenRouterClient.</summary>
        public IEnumerable<(string Name, string Description, string ParametersJsonSchema)> ToolSpecs() =>
            _tools.Values.Select(t => (t.Name, t.Description, t.ParametersJsonSchema));
    }
}
