using System;
using System.Linq;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>Un giocatore risolto dal database, con la variante di comando kick CORRETTA per come è stato cercato.</summary>
    public class ResolvedPlayer
    {
        public long CharId;
        public string CharName;
        public long UserUid;
        public string UserId;
        /// <summary>kickcn/kickun/kickcid/kickuid — determinato da QUALE colonna ha fatto match, mai da un'ipotesi.</summary>
        public string Command;
    }

    /// <summary>
    /// Risolve un target (nome personaggio, username, CharID o UserUID) contro
    /// <c>PS_GameData.dbo.Chars</c> — colma il bug reale osservato: il modello sceglieva `command` a
    /// caso tra le 4 varianti kick (es. `kickun` con un nome-personaggio invece dello username reale),
    /// il server rifiutava silenziosamente (rc SQL=0 ma nessun effetto). Query read-only via
    /// <see cref="AiSqlReader"/> (login ShaiyaAiReader) — mai scrittura, mai fidarsi della scelta del
    /// modello: il comando usato per il kick è SEMPRE quello determinato qui, non quello richiesto.
    /// </summary>
    public static class PlayerLookup
    {
        public static bool TryResolve(HubSettings settings, string target, out ResolvedPlayer resolved, out string error)
        {
            resolved = null;
            error = null;
            if (string.IsNullOrWhiteSpace(target)) { error = "Target vuoto."; return false; }

            // Target già validato dal charset sicuro di Invariants.CheckKickPlayer prima di arrivare
            // qui, ma raddoppia comunque l'escape apici per difesa in profondità (mai fidarsi di un
            // solo livello di validazione per una stringa che finisce in una query SQL).
            string escaped = target.Replace("'", "''");
            string sql = "SELECT TOP 1 CharID, CharName, UserUID, UserID FROM PS_GameData.dbo.Chars WHERE " +
                         $"CharName = '{escaped}' OR UserID = '{escaped}' OR " +
                         $"CAST(CharID AS NVARCHAR(20)) = '{escaped}' OR CAST(UserUID AS NVARCHAR(20)) = '{escaped}';";

            int rc = AiSqlReader.Query(settings, "PS_GameData", sql, out string output);
            if (rc != 0) { error = "Lookup DB fallito: " + output; return false; }

            var lines = (output ?? "").Replace("\r", "").Split('\n');
            string dataLine = null;
            for (int i = 2; i < lines.Length; i++) // riga 0=header, riga 1=separatore "----"
            {
                string l = lines[i].Trim();
                if (l.Length == 0) continue;
                if (l.StartsWith("(")) break; // "(N rows affected)": fine dati
                dataLine = l;
                break;
            }
            if (dataLine == null)
            {
                error = $"Nessun giocatore trovato per '{target}' (cercato come nome personaggio, username, CharID e UserUID) — comando NON inviato.";
                return false;
            }

            var cols = dataLine.Split('|');
            if (cols.Length < 4) { error = "Formato risposta DB inatteso: " + dataLine; return false; }

            var p = new ResolvedPlayer
            {
                CharId = long.TryParse(cols[0].Trim(), out var cid) ? cid : 0,
                CharName = cols[1].Trim(),
                UserUid = long.TryParse(cols[2].Trim(), out var uuid) ? uuid : 0,
                UserId = cols[3].Trim()
            };

            if (string.Equals(target, p.CharName, StringComparison.OrdinalIgnoreCase)) p.Command = "kickcn";
            else if (string.Equals(target, p.UserId, StringComparison.OrdinalIgnoreCase)) p.Command = "kickun";
            else if (long.TryParse(target, out var t1) && t1 == p.CharId) p.Command = "kickcid";
            else if (long.TryParse(target, out var t2) && t2 == p.UserUid) p.Command = "kickuid";
            else p.Command = "kickcn"; // difensivo: non dovrebbe mai succedere dato il WHERE sopra

            resolved = p;
            return true;
        }
    }
}
