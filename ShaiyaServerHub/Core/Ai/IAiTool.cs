namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Tier di governance di un'azione AI (vedi PLAN §Modello di autonomia). In P1 esistono SOLO tool
    /// T0 (osservazione): nessuna azione che muta stato è ancora esposta. I tier T1-T3 e il PolicyGate
    /// che li applica arrivano in P2/P3.
    /// </summary>
    public enum AiToolTier { T0_Observe, T1_AutoHeal, T2_Defense, T3_Risky }

    /// <summary>Contesto condiviso passato a ogni tool alla costruzione (via ToolRegistry).</summary>
    public class AiToolContext
    {
        public HubSettings Settings;
    }

    /// <summary>
    /// Contratto di una "capability" esposta all'AI. Ogni nuova funzione del Hub che deve diventare
    /// visibile all'AI implementa questa interfaccia in una classe pubblica non-astratta con un
    /// costruttore (AiToolContext) — <see cref="ToolRegistry"/> la scopre via reflection all'avvio,
    /// NESSUNA registrazione manuale altrove. Vedi CLAUDE.md "come aggiungere una capability all'AI".
    /// </summary>
    public interface IAiTool
    {
        /// <summary>Nome univoco, stabile, usato dal modello per chiamare il tool (snake_case).</summary>
        string Name { get; }
        /// <summary>Descrizione per il system prompt / schema tool-calling.</summary>
        string Description { get; }
        /// <summary>Tier di governance (vedi <see cref="AiToolTier"/>).</summary>
        AiToolTier Tier { get; }
        /// <summary>True se l'effetto del tool è annullabile (rilevante per i tier T1+ in P2/P3).</summary>
        bool Reversible { get; }
        /// <summary>JSON Schema (stile OpenAI function-calling) dei parametri. "{}" se nessuno.</summary>
        string ParametersJsonSchema { get; }
        /// <summary>Esegue il tool. Non deve mai lanciare: cattura ed espone l'errore nel testo di ritorno.</summary>
        string Execute(string argumentsJson);
    }
}
