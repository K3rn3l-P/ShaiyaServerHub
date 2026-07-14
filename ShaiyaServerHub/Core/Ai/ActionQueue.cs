using System;
using System.Collections.Generic;
using System.Linq;

namespace ShaiyaServerHub.Core.Ai
{
    public enum PendingActionStatus { Pending, Approved, Denied, TakenOver, AutoExecuted, Failed, Expired }

    /// <summary>Un'azione T2+ in attesa di esecuzione — approvata da Telegram o eseguita da sola a grace scaduta.</summary>
    public class PendingAction
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string ToolName;
        public string ArgumentsJson;
        /// <summary>Motivazione del modello (dal parametro "reason"/"reasoning" del tool) — mostrata in Telegram.</summary>
        public string Reasoning;
        public DateTime CreatedAt = DateTime.UtcNow;
        /// <summary>Null = richiede SEMPRE una decisione esplicita (RequireApproval). Valorizzata = auto-eseguibile a scadenza.</summary>
        public DateTime? GraceDeadline;
        public PendingActionStatus Status = PendingActionStatus.Pending;
        /// <summary>Esito testuale dopo l'esecuzione (successo/errore del tool) — per audit/notifica finale.</summary>
        public string ResultText;
    }

    /// <summary>
    /// Coda in-memory delle azioni T2+ in attesa — vedi PLAN §Componenti codice. Persistenza SOLO in
    /// processo (nessun file): se l'Hub riavvia, le azioni pendenti si perdono — accettabile per questo
    /// blocco (azioni reversibili, basso rischio se una richiesta di approvazione va persa invece che
    /// esser rieseguita); una persistenza su disco è un miglioramento futuro, non un prerequisito di
    /// sicurezza (gli invarianti hard restano verificati ad ogni esecuzione, non solo all'accodamento).
    /// </summary>
    public static class ActionQueue
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, PendingAction> _items = new Dictionary<string, PendingAction>();

        public static PendingAction Enqueue(string toolName, string argumentsJson, string reasoning, DateTime? graceDeadline)
        {
            var item = new PendingAction
            {
                ToolName = toolName,
                ArgumentsJson = argumentsJson,
                Reasoning = reasoning,
                GraceDeadline = graceDeadline
            };
            lock (_lock) { _items[item.Id] = item; }
            return item;
        }

        public static bool TryGet(string id, out PendingAction item)
        {
            lock (_lock) return _items.TryGetValue(id ?? "", out item);
        }

        /// <summary>Applica una decisione umana (da bottone Telegram) se l'azione è ancora Pending.</summary>
        public static bool TryResolve(string id, PendingActionStatus newStatus)
        {
            lock (_lock)
            {
                if (!_items.TryGetValue(id ?? "", out var item) || item.Status != PendingActionStatus.Pending)
                    return false;
                item.Status = newStatus;
                return true;
            }
        }

        public static void MarkResult(string id, PendingActionStatus finalStatus, string resultText)
        {
            lock (_lock)
            {
                if (_items.TryGetValue(id ?? "", out var item))
                {
                    item.Status = finalStatus;
                    item.ResultText = resultText;
                }
            }
        }

        /// <summary>Azioni ancora Pending con grace scaduta — da eseguire automaticamente.</summary>
        public static List<PendingAction> GetDueForAutoExecution()
        {
            var now = DateTime.UtcNow;
            lock (_lock)
                return _items.Values
                    .Where(a => a.Status == PendingActionStatus.Pending && a.GraceDeadline.HasValue && a.GraceDeadline.Value <= now)
                    .ToList();
        }

        public static List<PendingAction> GetPending()
        {
            lock (_lock) return _items.Values.Where(a => a.Status == PendingActionStatus.Pending).ToList();
        }
    }
}
