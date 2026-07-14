using System;
using System.Threading;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Timer dedicato (30s) che rimuove i blocchi IP temporanei scaduti da <see cref="TempBlockRegistry"/>
    /// e risincronizza l'unica regola firewall (<see cref="FirewallManager.SyncTempBlockRule"/>) —
    /// SEMPRE, indipendente da <c>AiMasterEnabled</c>/kill-switch: rimuovere un blocco è l'azione SICURA
    /// (undo), non deve mai restare bloccata da un interruttore pensato per governare solo le azioni che
    /// METTONO in atto un blocco. Avviato da <c>AiControl.Init</c> come <see cref="ApprovalWatchdog"/>/
    /// <see cref="WatchLoop"/>, vive quanto il processo Hub.
    /// </summary>
    public class TempBlockScheduler : IDisposable
    {
        private readonly HubSettings _settings;
        private readonly Timer _timer;
        private volatile bool _busy;

        public TempBlockScheduler(HubSettings settings)
        {
            _settings = settings;
            _timer = new Timer(Tick, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private void Tick(object state)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var expired = TempBlockRegistry.GetExpired();
                if (expired.Count == 0) return;

                foreach (var e in expired) TempBlockRegistry.Remove(e.Ip);

                var fw = new FirewallManager();
                string log = "";
                bool ok = fw.SyncTempBlockRule(TempBlockRegistry.ActiveIps(), msg => log += msg + "\n");

                foreach (var e in expired)
                {
                    string outcome = ok ? "regola risincronizzata" : "ERRORE risincronizzazione — verificare a mano nel Firewall di Windows: " + log;
                    App.WriteLog("AI-TEMPBLOCK", $"Blocco temporaneo IP {e.Ip} scaduto — {outcome}.");
                    Notifier.NotifyInfo(_settings, $"⏱ Blocco temporaneo IP {e.Ip} scaduto — {outcome}.");
                }
            }
            catch (Exception ex) { App.WriteLog("AI-TEMPBLOCK", "errore tick: " + ex.Message); }
            finally { _busy = false; }
        }

        public void Dispose() => _timer?.Dispose();
    }
}
