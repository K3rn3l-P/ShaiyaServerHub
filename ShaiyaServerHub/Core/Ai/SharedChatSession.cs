using System.Collections.Generic;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Storico chat CONDIVISO tra la chat del Hub UI (<see cref="ShaiyaServerHub.Controls.AiControl"/>) e
    /// la chat testo libero via Telegram (<see cref="ApprovalWatchdog"/>) — richiesta esplicita
    /// dell'utente: "se faccio una domanda che riguarda una history scritta dall'Hub, vorrei che Telegram
    /// la leggesse anche, e viceversa". Un solo turno alla volta (<see cref="TryBeginTurn"/>): se arriva
    /// un messaggio da un canale mentre l'altro sta già elaborando un turno, il secondo NON parte (evita
    /// due chiamate concorrenti che mutano la stessa history) — l'utente riceve un avviso e ripete la
    /// richiesta. NON riguarda i giri proattivi di WatchLoop/SecurityDigestScheduler/AutorunsAuditScheduler
    /// (quelli restano come prima: prompt sintetico, nessuna history condivisa, indipendenti da questo lock).
    /// </summary>
    public static class SharedChatSession
    {
        private static readonly object Lock = new object();
        private static readonly List<(string Role, string Content)> History = new List<(string, string)>();
        private static volatile bool _turnInProgress;

        public static bool TryBeginTurn()
        {
            lock (Lock)
            {
                if (_turnInProgress) return false;
                _turnInProgress = true;
                return true;
            }
        }

        public static void EndTurn() => _turnInProgress = false;

        public static List<(string Role, string Content)> Snapshot()
        {
            lock (Lock) return new List<(string, string)>(History);
        }

        public static void Append(string role, string content)
        {
            lock (Lock)
            {
                History.Add((role, content));
                while (History.Count > 20) History.RemoveAt(0);
            }
        }

        public static void Clear()
        {
            lock (Lock) History.Clear();
        }
    }
}
