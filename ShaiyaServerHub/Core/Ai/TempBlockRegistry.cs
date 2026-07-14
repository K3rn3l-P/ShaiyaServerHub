using System;
using System.Collections.Generic;
using System.Linq;

namespace ShaiyaServerHub.Core.Ai
{
    public class TempBlockEntry
    {
        public string Ip;
        public DateTime ExpiresUtc;
        public string Reason;
    }

    /// <summary>
    /// Stato in-memory dei blocchi IP temporanei attivi creati da <c>temp_block_ip</c> — stesso limite
    /// di <see cref="ActionQueue"/> (non persistito su riavvio Hub, accettabile per un'azione T2
    /// reversibile a basso rischio: allo scadere il blocco sarebbe comunque scaduto). Fonte di verità
    /// per <see cref="FirewallManager.SyncTempBlockRule"/> — UNA sola regola firewall reale, l'insieme
    /// di IP qui dentro decide chi ci sta dentro.
    /// </summary>
    public static class TempBlockRegistry
    {
        private static readonly List<TempBlockEntry> _active = new List<TempBlockEntry>();
        private static readonly object _lock = new object();

        public static void Add(string ip, DateTime expiresUtc, string reason)
        {
            lock (_lock)
            {
                _active.RemoveAll(e => string.Equals(e.Ip, ip, StringComparison.OrdinalIgnoreCase));
                _active.Add(new TempBlockEntry { Ip = ip, ExpiresUtc = expiresUtc, Reason = reason });
            }
        }

        public static bool TryGet(string ip, out TempBlockEntry entry)
        {
            lock (_lock) { entry = _active.FirstOrDefault(e => string.Equals(e.Ip, ip, StringComparison.OrdinalIgnoreCase)); }
            return entry != null;
        }

        public static void Remove(string ip)
        {
            lock (_lock) { _active.RemoveAll(e => string.Equals(e.Ip, ip, StringComparison.OrdinalIgnoreCase)); }
        }

        public static List<TempBlockEntry> GetExpired()
        {
            lock (_lock) { return _active.Where(e => e.ExpiresUtc <= DateTime.UtcNow).ToList(); }
        }

        /// <summary>Tutti gli IP attualmente attivi — usato per ricalcolare l'intera regola firewall ad ogni add/remove.</summary>
        public static List<string> ActiveIps()
        {
            lock (_lock) { return _active.Select(e => e.Ip).ToList(); }
        }

        public static List<TempBlockEntry> Active()
        {
            lock (_lock) { return _active.OrderBy(e => e.ExpiresUtc).ToList(); }
        }
    }
}
