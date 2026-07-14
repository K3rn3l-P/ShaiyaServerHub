using System.IO;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Load/save di ai\config\models.json. Punto unico usato sia da ModelRouter (runtime, sola
    /// lettura) sia dalle Impostazioni AI del Hub (UI, lettura+scrittura) — così i 3 slot modello
    /// (Critical/Default/Fallback) restano modificabili sia a mano nel file sia dalla UI, coerenti.
    /// </summary>
    public static class ModelsConfigFile
    {
        private static string Path_ => Path.Combine(AiPaths.ConfigDir, "models.json");

        public static AiModelSlots Load()
        {
            try
            {
                if (File.Exists(Path_))
                {
                    string json = File.ReadAllText(Path_);
                    return JsonSerializer.Deserialize<AiModelSlots>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AiModelSlots();
                }
            }
            catch { /* config assente/corrotta → default */ }
            return new AiModelSlots();
        }

        public static void Save(AiModelSlots slots)
        {
            try
            {
                AiPaths.EnsureFolders();
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(Path_, JsonSerializer.Serialize(slots ?? new AiModelSlots(), opts));
            }
            catch { }
        }
    }
}
