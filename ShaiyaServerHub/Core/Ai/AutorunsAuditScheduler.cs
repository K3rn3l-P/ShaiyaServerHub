using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Audit persistenza/ASEP GIORNALIERO via autorunsc (Sysinternals) — colma il gap "config-drift"
    /// mai coperto dal digest 15 min (<see cref="SecurityDigestScheduler"/>): una scansione autorunsc
    /// (100+ location) è più pesante dei sensori leggeri di quel giro, quindi gira 1 volta al giorno,
    /// non ad ogni tick. Riusa lo STESSO script/baseline della Test Suite
    /// (<see cref="ExternalToolsManager.RunAutorunsBaselineCheck"/>, 22_autoruns_baseline.ps1) — nessuna
    /// logica di parsing duplicata, badge Test Suite e giro automatico condividono un'unica fonte.
    ///
    /// Anti-spam: notifica Telegram SOLO se il testo del finding WARN è DIVERSO dall'ultimo notificato
    /// (hash persistito) — un drift già segnalato ieri e non ancora risolto/accettato dall'admin non
    /// manda un secondo messaggio identico ogni giorno, ma una NUOVA voce (testo diverso) sì. Stesso
    /// principio "alert solo su anomalia" di SecurityDigestScheduler, applicato qui come dedup per
    /// contenuto invece che per soglia di gravità (qui c'è un solo controllo, non tanti sensori diversi).
    ///
    /// Intervalli letti da <see cref="HubSettings"/> (Impostazioni → scheda AI → gruppo "Autorun /
    /// persistenza"), non hardcoded — <see cref="_timer"/> si ri-schedula da sé ad ogni tick (Timer
    /// non-periodico, <see cref="Timeout.InfiniteTimeSpan"/> + <c>Change()</c> in fondo al giro) così un
    /// cambio di CheckInterval dalle Impostazioni si applica dal giro successivo, senza dover ricreare
    /// lo scheduler.
    /// </summary>
    public class AutorunsAuditScheduler : IDisposable
    {
        private readonly HubSettings _settings;
        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly Timer _timer;
        private volatile bool _busy;

        private static string MarkerFile => Path.Combine(ExternalToolsManager.ToolsDir, "autoruns-lastrun.txt");
        private static string LastNotifiedHashFile => Path.Combine(ExternalToolsManager.ToolsDir, "autoruns-lastnotified.txt");

        private TimeSpan CheckInterval => TimeSpan.FromMinutes(Math.Max(1, _settings.AiAutorunsCheckIntervalMinutes));
        private TimeSpan RunEvery => TimeSpan.FromHours(Math.Max(1, _settings.AiAutorunsRunEveryHours));
        private TimeSpan StartupGrace => TimeSpan.FromMinutes(Math.Max(0, _settings.AiAutorunsStartupGraceMinutes));

        public AutorunsAuditScheduler(HubSettings settings)
        {
            _settings = settings;
            _timer = new Timer(Tick, null, CheckInterval, Timeout.InfiniteTimeSpan);
        }

        private void Tick(object state)
        {
            if (_busy) { RescheduleNext(); return; }
            _busy = true;
            try
            {
                if (!_settings.AiMasterEnabled || Invariants.KillSwitchActive) return;
                if (DateTime.UtcNow - _startedUtc < StartupGrace) return; // stesso motivo di SecurityDigestScheduler: servizi/tool potrebbero non essere pronti appena dopo un riavvio
                if (!ExternalToolsManager.IsAutorunscPresent) return; // non scaricato: nessun errore/spam, l'admin lo vede come INFO in Test Suite

                if (File.Exists(MarkerFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(MarkerFile) < RunEvery)
                    return; // non ancora ora del giro

                ExternalToolsManager.RunAutorunsBaselineCheck(out var status, out var detail);
                Directory.CreateDirectory(ExternalToolsManager.ToolsDir);
                File.WriteAllText(MarkerFile, DateTime.UtcNow.ToString("o"));
                App.WriteLog("AI-AUTORUNS", $"[{status}] {detail}");

                if (!string.Equals(status, "WARN", StringComparison.OrdinalIgnoreCase))
                {
                    // Giro pulito (o INFO/ERRORE non azionabile): reset del dedup, così una futura
                    // ricomparsa di un WARN già visto in passato notifica di nuovo (non è più "la stessa
                    // situazione mai risolta", la situazione è tornata pulita in mezzo).
                    try { if (File.Exists(LastNotifiedHashFile)) File.Delete(LastNotifiedHashFile); } catch { }
                    return;
                }

                string hash = Sha256(detail);
                string lastHash = File.Exists(LastNotifiedHashFile) ? File.ReadAllText(LastNotifiedHashFile) : null;
                if (hash == lastHash) return; // stesso identico drift già notificato: non spammare ogni giorno

                File.WriteAllText(LastNotifiedHashFile, hash);
                Notifier.NotifyDigest(_settings, FindingSeverity.Medium,
                    $"Audit persistenza/autostart (autorunsc, giro giornaliero): {detail}");
            }
            catch (Exception ex) { App.WriteLog("AI-AUTORUNS", "errore tick: " + ex.Message); }
            finally { _busy = false; RescheduleNext(); }
        }

        private void RescheduleNext()
        {
            try { _timer.Change(CheckInterval, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
        }

        private static string Sha256(string s)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")));
        }

        public void Dispose() => _timer?.Dispose();
    }
}
