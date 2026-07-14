using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub.Controls
{
    /// <summary>
    /// Vista "Key Rotation": stato della rotazione pianificata (task 4:00), rotazione on-demand
    /// ("Ruota ora" esegue Rotate-ShaiyaKeys.ps1, NIENTE riavvio → banner "riavvia il server"),
    /// fingerprint delle chiavi correnti (mai in chiaro) e gestione del task pianificato.
    /// La crypto ESAH è integrata nell'Hub (Core.SahCrypt); il ps1 la usa via CLI (--sah-encrypt/-decrypt).
    /// </summary>
    public partial class KeyRotationControl : UserControl
    {
        private HubSettings _settings;
        private ScriptRunner _runner;
        private string _ps1Path;
        private string _iniPath;
        private string _logPath;

        // Task pianificato gestito dall'Hub (cartella \ShaiyaServerHub\).
        private const string TaskName = KeyRotationReport.TaskName;
        private static string TaskFullName => $"\\{TaskSchedulerService.Folder}\\{TaskName}";

        public KeyRotationControl() { InitializeComponent(); }

        public void Init(HubSettings settings)
        {
            _settings = settings;
            _runner = new ScriptRunner(settings);

            var repo = ScriptRepository.KeyRotation;
            repo.EnsureFolders();
            _ps1Path = Path.Combine(repo.Ps1Dir, "Rotate-ShaiyaKeys.ps1");
            _logPath = Path.Combine(repo.Root, "keyrotation.log");
            _iniPath = Path.Combine(_settings.ServerBinPath ?? "", "Data", "GMSecurity.ini");

            RefreshAll();
        }

        // ── Refresh ──────────────────────────────────────────
        public async void RefreshAll()
        {
            RefreshKeys();
            await RefreshScheduleAsync();
        }

        private async Task RefreshScheduleAsync()
        {
            TbSchedState.Text = L.KrChecking;
            TbSchedState.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x81));
            TbSchedNext.Text = "";

            var rep = await Task.Run(() => KeyRotationReport.Load(_iniPath));
            if (SchedDot == null) return;

            if (!rep.TaskInstalled)
            {
                SchedDot.Fill = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x7A));
                TbSchedState.Text = L.KrTaskNotInstalled;
                TbSchedState.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD0, 0xD8));
                TbSchedNext.Text = "";
                BtnInstallTask.IsEnabled = true;
                BtnEnableTask.IsEnabled = BtnDisableTask.IsEnabled = false;
                return;
            }

            // Colore pallino da esito ultimo run.
            Color dot = rep.LastRun == TaskLogState.Error ? Color.FromRgb(0xF4, 0x6A, 0x6A)
                      : rep.LastRun == TaskLogState.Ok ? Color.FromRgb(0x4D, 0xC8, 0x70)
                      : Color.FromRgb(0xE8, 0xC8, 0x81);
            SchedDot.Fill = new SolidColorBrush(dot);
            TbSchedState.Text = L.KrTaskState(rep.TaskEnabled, rep.LastRun);
            TbSchedState.Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xE0, 0xE0));
            TbSchedNext.Text = L.KrNextRun(rep.NextRun);

            BtnInstallTask.IsEnabled = true;   // reinstalla/aggiorna sempre consentito
            BtnEnableTask.IsEnabled = !rep.TaskEnabled;
            BtnDisableTask.IsEnabled = rep.TaskEnabled;
        }

        /// <summary>Fingerprint delle chiavi correnti (SHA-256[:8] del valore, MAI in chiaro).</summary>
        private void RefreshKeys()
        {
            string content = null;
            try { if (File.Exists(_iniPath)) content = File.ReadAllText(_iniPath); }
            catch { }

            if (content == null)
            {
                TbKeyClient.Text = TbKeyToken.Text = TbKeyPatch.Text = TbKeySah.Text = "";
                TbKeyMeta.Text = L.KrIniMissing(_iniPath);
                return;
            }

            TbKeyClient.Text = L.KrKeyLine("ClientSecret", Fingerprint(content, "ClientSecret"));
            TbKeyToken.Text = L.KrKeyLine("TokenSecret", Fingerprint(content, "TokenSecret"));
            TbKeyPatch.Text = L.KrKeyLine("PatchKeyHex", Fingerprint(content, "PatchKeyHex"));
            TbKeySah.Text = L.KrKeyLine("SahKeyHex", Fingerprint(content, "SahKeyHex")) + "  " + L.KrSahNote;

            DateTime mtime = File.GetLastWriteTime(_iniPath);
            TbKeyMeta.Text = L.KrKeysMeta(mtime);
        }

        // "Key=value" → SHA-256[:8] del valore (o null se assente). Non rivela il segreto.
        private static string Fingerprint(string iniContent, string key)
        {
            var m = Regex.Match(iniContent, "(?m)^" + Regex.Escape(key) + "=(.*)$");
            if (!m.Success) return null;
            string val = m.Groups[1].Value.Trim();
            if (val.Length == 0) return null;
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(val));
                return BitConverter.ToString(h, 0, 4).Replace("-", "").ToLowerInvariant();
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => RefreshAll();

        // ── Ruota ora → esegue il ps1 (rotazione + reload ps_game/ps_login gestito DAL ps1) ─────
        // Il riavvio dei servizi è nel ps1 così "Ruota ora" e il task 04:00 usano la STESSA logica.
        private async void BtnRotateNow_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(Window.GetWindow(this), L.KrConfirmRotate, L.KrTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!File.Exists(_ps1Path)) { Log(L.KrScriptMissing(_ps1Path)); return; }

            RebootBanner.Visibility = Visibility.Collapsed;
            BtnRotateNow.IsEnabled = false;
            try
            {
                Log("── " + L.KrRotating + " ──");

                bool rebootNeeded = false;
                int rc = -1;
                await Task.Run(() =>
                {
                    rc = _runner.RunPowerShell(_ps1Path, line =>
                    {
                        Log(line);
                        // Il ps1 emette questo marker solo se NON è riuscito a riavviare i servizi.
                        if (line != null && line.IndexOf("RIAVVIO-RICHIESTO", StringComparison.OrdinalIgnoreCase) >= 0)
                            rebootNeeded = true;
                    });
                });

                Log(rc == 0 ? L.KrRotateDone : L.KrRotateErr(rc));
                if (rebootNeeded) RebootBanner.Visibility = Visibility.Visible;
                RefreshAll();
            }
            finally { BtnRotateNow.IsEnabled = true; }
        }

        // ── Modifica ps1 ─────────────────────────────────────
        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_ps1Path))
                    new EditorWindow(_ps1Path) { Owner = Window.GetWindow(this) }.ShowDialog();
                else
                    Process.Start("explorer.exe", Path.GetDirectoryName(_ps1Path));
            }
            catch (Exception ex) { Log(L.Err(ex.Message)); }
        }

        // ── Task pianificato (installa / abilita / disabilita) ─
        private async void BtnInstallTask_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_ps1Path)) { Log(L.KrScriptMissing(_ps1Path)); return; }
            if (MessageBox.Show(Window.GetWindow(this), L.KrConfirmInstall, L.KrTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                // Azione: ShaiyaServerHub.exe --run "<ps1>" --log "<log\KeyRotation.log>" (headless, esito leggibile).
                string exe = TaskSchedulerService.ExePath;
                string logFile = TaskLogReader.LogPathFor(TaskName);
                string args = $"--run \"{_ps1Path}\" --log \"{logFile}\"";
                var spec = new ScheduleSpec { Type = "DAILY", Time = "04:00" };

                Log("── " + L.KrInstalling + " ──");
                int rc = -1;
                // Il task esegue il ps1 headless (--run): rotazione + riavvio ps_game/ps_login gestiti DAL ps1.
                // Nessuna azione di reboot del server: bastano i due servizi.
                await Task.Run(() => rc = TaskSchedulerService.Create(
                    TaskName, spec, exe, args, TaskSchedulerService.ExeDir, Log));
                Log(rc == 0 ? L.KrInstallDone : L.KrRotateErr(rc));
                RefreshAll();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnEnableTask_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                int rc = -1;
                await Task.Run(() => rc = TaskSchedulerService.SetEnabled(TaskFullName, true, Log));
                RefreshAll();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnDisableTask_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                int rc = -1;
                await Task.Run(() => rc = TaskSchedulerService.SetEnabled(TaskFullName, false, Log));
                RefreshAll();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        // ── Apri log / cartella Data ─────────────────────────
        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            string content;
            try
            {
                content = File.Exists(_logPath)
                    ? File.ReadAllText(_logPath)
                    : L.KrNoLog;
            }
            catch (Exception ex) { content = L.Err(ex.Message); }
            new LogWindow("keyrotation.log", content) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void BtnOpenData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = Path.GetDirectoryName(_iniPath);
                if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
                else Log(L.KrIniMissing(_iniPath));
            }
            catch (Exception ex) { Log(L.Err(ex.Message)); }
        }

        // ── Log embedded (UI + file dell'area) ───────────────
        private void Log(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Log(msg))); return; }
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            TbLog.AppendText(line + "\n");
            TbLog.ScrollToEnd();
            try { File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8); } catch { }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TbLog.Clear();
    }
}
