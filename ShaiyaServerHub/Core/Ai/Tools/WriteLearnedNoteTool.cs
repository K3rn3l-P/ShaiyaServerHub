using System;
using System.IO;
using System.Text.Json;

namespace ShaiyaServerHub.Core.Ai.Tools
{
    /// <summary>
    /// Chiude il pezzo "auto-apprendimento" del piano P3 (§Efficacia, anti-falsi-positivi): finora
    /// <see cref="OutcomeTracker"/> registrava OGNI esito T1+/T2 in un log strutturato
    /// (<c>ai\knowledge\learned\outcomes.log</c>), ma quel file NON è mai stato riletto nel contesto
    /// (<see cref="KnowledgeStore.LoadAll"/> carica solo i <c>*.md</c> di <c>knowledge\</c>+
    /// <c>knowledge\learned\</c> — <c>.log</c> escluso di proposito, troppo grezzo). Questo tool dà
    /// all'AI un modo di scrivere note in PROSA, leggibili da sé stessa nei turni futuri E dall'admin
    /// (sono semplici file .md, editabili/cancellabili a mano, leggibili anche con
    /// <see cref="ReadHubFileTool"/>) — es. "questo pattern è un falso positivo noto", "questo account
    /// è di test dell'admin", un fatto scoperto durante un'indagine che varrà la pena ricordare.
    /// T1 (non T0): scrivere un file è comunque una mutazione, anche se innocua e mai eseguibile (pura
    /// prosa, mai codice) — reversibile (l'admin può editare/cancellare liberamente i file in
    /// ai\knowledge\learned\). A differenza di write_ai_test_script, qui l'APPEND è permesso (le note
    /// su uno stesso argomento si accumulano nel tempo, non hanno senso "una sola versione immutabile"
    /// come uno script).
    /// </summary>
    public class WriteLearnedNoteTool : IAiTool
    {
        private const long MaxFileBytes = 20_000;

        public string Name => "write_learned_note";
        public string Description => "Scrive (o aggiunge a) una nota in prosa in ai\\knowledge\\learned\\<topic>.md — " +
            "letta automaticamente nei TURNI FUTURI (tuoi e di altre sessioni) come knowledge base. Usala SOLO per " +
            "pattern che varrà la pena ricordare: un falso positivo confermato (es. \"RANGE_HACK_PVE è rumore noto su " +
            "questo server\"), un account chiarito come di test dall'admin, un fatto scoperto durante un'indagine che " +
            "cambierebbe il tuo giudizio futuro. NON usarla per fatti banali/temporanei (es. \"il server ha 3 giocatori " +
            "online ora\") — quello è già osservabile con i tool, non serve persistere. 'topic' è il nome file (solo " +
            "lettere/numeri/trattini), le note sullo stesso topic si accumulano nel tempo. Azione T1, reversibile " +
            "(l'admin può editare/cancellare i file liberamente).";
        public AiToolTier Tier => AiToolTier.T1_AutoHeal;
        public bool Reversible => true;
        public string ParametersJsonSchema =>
            "{\"type\":\"object\",\"properties\":{" +
            "\"topic\":{\"type\":\"string\",\"description\":\"Nome file/argomento (solo lettere/numeri/trattini), es. 'false-positive-range-hack-pve'\"}," +
            "\"note\":{\"type\":\"string\",\"description\":\"La nota in prosa da ricordare (max 4000 caratteri)\"}}," +
            "\"required\":[\"topic\",\"note\"]}";

        public string Execute(string argumentsJson)
        {
            try
            {
                string topic = null, note = null;
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("topic", out var t) && t.ValueKind == JsonValueKind.String) topic = t.GetString();
                    if (root.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String) note = n.GetString();
                }

                // Ri-verifica anche qui (difesa in profondità, come per gli altri tool T1+).
                if (!Invariants.CheckWriteLearnedNote(topic, note, out var denyReason))
                    return "RIFIUTATO: " + denyReason;

                Directory.CreateDirectory(AiPaths.LearnedDir);
                string path = Path.Combine(AiPaths.LearnedDir, topic + ".md");

                bool isNew = !File.Exists(path);
                if (!isNew)
                {
                    var fi = new FileInfo(path);
                    if (fi.Length + note.Length > MaxFileBytes)
                        return $"RIFIUTATO: '{topic}.md' ha già raggiunto il limite dimensione ({MaxFileBytes / 1024} KB) — " +
                               "usa un topic diverso o chiedi all'admin di ripulire le note vecchie.";
                }

                string entry = $"### {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n{note}\n\n";
                if (isNew) File.WriteAllText(path, $"# {topic}\n\n" + entry);
                else File.AppendAllText(path, entry);

                return $"Nota salvata in ai\\knowledge\\learned\\{topic}.md — sarà nel contesto dei turni futuri.";
            }
            catch (Exception ex) { return "ERRORE: " + ex.Message; }
        }
    }
}
