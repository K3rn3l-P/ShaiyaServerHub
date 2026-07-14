using System;
using ShaiyaServerHub.Core.Ai.Sensors;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>Tool T0: spazio disco sui volumi fissi — vedi PLAN §P4, <see cref="DiskSpaceSensor"/>.</summary>
    public class DiskSpaceTool : IAiTool
    {
        public string Name => "disk_space";
        public string Description => "Spazio disco libero/totale sui volumi fissi del server (C:, ecc.). " +
            "Nessun parametro. Read-only.";
        public AiToolTier Tier => AiToolTier.T0_Observe;
        public bool Reversible => true;
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{}}";

        public string Execute(string argumentsJson)
        {
            try
            {
                DiskSpaceSensor.Query(out var output);
                return string.IsNullOrWhiteSpace(output) ? "Nessun dato disco disponibile." : output;
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
