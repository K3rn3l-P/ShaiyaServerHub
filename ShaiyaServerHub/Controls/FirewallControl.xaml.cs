using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub.Controls
{
    /// <summary>
    /// Scheda "Windows Firewall" della vista Funzioni: stato profili + on/off,
    /// regole Shaiya-* (posture consigliata + regole custom, edit/rimozione),
    /// e test/audit della superficie di rete con log su file e pallino esito per regola.
    /// </summary>
    public partial class FirewallControl : UserControl
    {
        private readonly FirewallManager _fw = new FirewallManager();
        private HubSettings _settings;
        private string _scriptPath;
        private string _auditLogPath;
        private readonly ObservableCollection<FirewallRuleRow> _rules = new ObservableCollection<FirewallRuleRow>();
        private List<PortAuditRow> _lastAudit;

        public FirewallControl() { InitializeComponent(); }

        public void Init(HubSettings settings)
        {
            _settings = settings;
            TbLanCidr.Text = string.IsNullOrWhiteSpace(settings.FirewallLanCidr) ? "10.10.0.0/16" : settings.FirewallLanCidr;

            var repo = ScriptRepository.Function("windows-firewall");
            repo.EnsureFolders();
            _scriptPath = Path.Combine(repo.Ps1Dir, "Apply-ShaiyaFirewall.ps1");
            _auditLogPath = Path.Combine(repo.Root, "net-audit.log");

            GridRules.ItemsSource = _rules;
            RefreshAll();
        }

        private string Lan => string.IsNullOrWhiteSpace(TbLanCidr.Text) ? "10.10.0.0/16" : TbLanCidr.Text.Trim();

        // ── Refresh ──────────────────────────────────────────
        public async void RefreshAll()
        {
            await RefreshStatus();
            await RefreshRules();
            await RunAuditAsync();   // popola i pallini esito + scrive il log
        }

        private async Task RefreshStatus()
        {
            var st = await Task.Run(() => _fw.GetStatus());
            bool on = st.AnyEnabled;
            StatusDot.Fill = new SolidColorBrush(on
                ? Color.FromRgb(0x6B, 0xD9, 0x8A)
                : Color.FromRgb(0xF4, 0x6A, 0x6A));
            TbFwState.Text = L.FwStateLabel(on);
            TbProfiles.Text = string.Join("   ",
                st.Profiles.Select(p => $"{p.Name}={(p.Enabled ? L.FwOn : L.FwOff)}/{p.DefaultInbound}"));
            BtnToggleFw.Content = on ? L.FwDisableBtn : L.FwEnableBtn;
            SwBanner.Visibility = st.SimplewallFiltering ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task RefreshRules()
        {
            string lan = Lan;
            var rules = await Task.Run(() => _fw.LoadRules(lan));
            _rules.Clear();
            foreach (var r in rules) _rules.Add(r);
            if (_lastAudit != null) AnnotateRules(_lastAudit);
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => RefreshAll();

        // ── Firewall on/off ──────────────────────────────────
        private async void BtnToggleFw_Click(object sender, RoutedEventArgs e)
        {
            var st = await Task.Run(() => _fw.GetStatus());
            bool turningOn = !st.AnyEnabled;
            string msg = turningOn ? L.FwConfirmEnable : L.FwConfirmDisable;
            if (MessageBox.Show(Window.GetWindow(this), msg, L.FwTitle,
                MessageBoxButton.YesNo, turningOn ? MessageBoxImage.Question : MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                Log(turningOn ? "── Abilito firewall (tutti i profili) ──" : "── Disabilito firewall ──");
                bool ok = await Task.Run(() => _fw.SetFirewallEnabled(turningOn, Log));
                Log(ok ? L.FwDone : L.FwActionErr);
                RefreshAll();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private void BtnOpenWf_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("wf.msc") { UseShellExecute = true }); }
            catch (Exception ex) { Log("ERRORE apertura wf.msc: " + ex.Message); }
        }

        // ── Posture consigliata ──────────────────────────────
        private async void BtnApplyPosture_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_scriptPath)) { Log(L.FwScriptMissing(_scriptPath)); return; }
            string lan = Lan;
            if (MessageBox.Show(Window.GetWindow(this), L.FwConfirmPosture(lan), L.FwTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                _settings.FirewallLanCidr = lan;
                _settings.Save();

                Log("── Applico posture consigliata (LAN " + lan + ") ──");
                await Task.Run(() => _fw.ApplyPosture(_scriptPath, lan, Log));
                Log(L.FwDone);
                RefreshAll();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private void BtnOpenAuditLog_Click(object sender, RoutedEventArgs e)
        {
            string content;
            try
            {
                content = File.Exists(_auditLogPath)
                    ? File.ReadAllText(_auditLogPath)
                    : "(nessun log: esegui prima 'Test di rete')";
            }
            catch (Exception ex) { content = "ERRORE lettura log: " + ex.Message; }
            new LogWindow("net-audit.log", content) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void BtnOpenScript_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_scriptPath))
                    new EditorWindow(_scriptPath) { Owner = Window.GetWindow(this) }.ShowDialog();
                else
                    Process.Start("explorer.exe", Path.GetDirectoryName(_scriptPath));
            }
            catch (Exception ex) { Log("ERRORE: " + ex.Message); }
        }

        // ── Regole (add / edit / enable / remove) ────────────
        private async void BtnAddRule_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new FirewallRuleDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                string lan = Lan;
                Log($"── Aggiungo regola: {dlg.RuleName} ──");
                bool ok = await Task.Run(() => _fw.AddRule(dlg.RuleName, dlg.Protocol, dlg.LocalPort,
                                                           dlg.RuleAction, dlg.Scope, lan, Log));
                Log(ok ? L.FwDone : L.FwActionErr);
                await RefreshRules();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void RuleEdit_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var row = ((FrameworkElement)sender).DataContext as FirewallRuleRow;
            if (row == null) return;
            string lan = Lan;
            var dlg = new FirewallRuleDialog { Owner = Window.GetWindow(this) };
            dlg.LoadForEdit(row, lan);
            if (dlg.ShowDialog() != true) return;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                Log($"── Modifico regola: {dlg.EditDisplayName} ──");
                bool ok = await Task.Run(() => _fw.UpdateRule(dlg.EditDisplayName, dlg.Protocol, dlg.LocalPort,
                                                              dlg.RuleAction, dlg.Scope, lan, Log));
                Log(ok ? L.FwDone : L.FwActionErr);
                await RefreshRules();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void RuleEnabled_Click(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            var row = cb?.DataContext as FirewallRuleRow;
            if (row == null) return;
            bool target = cb.IsChecked == true;
            if (cb != null) cb.IsEnabled = false;
            try
            {
                Log($"── {(target ? "Abilito" : "Disabilito")}: {row.DisplayName} ──");
                bool ok = await Task.Run(() => _fw.SetRuleEnabled(row.DisplayName, target, Log));
                if (!ok) Log(L.FwActionErr);
                await RefreshRules();
            }
            finally { if (cb != null) cb.IsEnabled = true; }
        }

        private async void RuleRemove_Click(object sender, RoutedEventArgs e)
        {
            var row = ((FrameworkElement)sender).DataContext as FirewallRuleRow;
            if (row == null) return;
            if (MessageBox.Show(Window.GetWindow(this), L.FwConfirmRemove(row.DisplayName), L.FwTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                bool ok = await Task.Run(() => _fw.RemoveRule(row.DisplayName, Log));
                Log(ok ? L.FwDone : L.FwActionErr);
                await RefreshRules();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        // ── Test di rete ─────────────────────────────────────
        private void BtnNetTest_Click(object sender, RoutedEventArgs e)
        {
            try { new NetworkAuditWindow(RunAuditAsync) { Owner = Window.GetWindow(this) }.ShowDialog(); }
            catch (Exception ex) { Log("ERRORE apertura test rete: " + ex.Message); }
        }

        /// <summary>
        /// Esegue l'audit, scrive il log su file, aggiorna i pallini delle regole
        /// e ritorna righe + testo di riepilogo (usato anche dal modale).
        /// </summary>
        public async Task<(List<PortAuditRow> rows, string summary)> RunAuditAsync()
        {
            string lan = Lan;
            var res = await Task.Run(() =>
            {
                var list = _fw.NetworkAudit(lan, out bool fwOn, out bool sw);
                return new { list, fwOn, sw };
            });

            _lastAudit = res.list;
            AnnotateRules(res.list);
            WriteAuditLog(res.list, res.fwOn, res.sw);

            int risk = res.list.Count(r => r.Risk == "RISCHIO");
            int warn = res.list.Count(r => r.Risk == "ATTENZIONE");
            int ok = res.list.Count(r => r.Risk == "OK");
            string summary = L.FwAuditNote(res.fwOn, res.sw) + "  " + L.FwAuditCounts(risk, warn, ok);
            return (res.list, summary);
        }

        // Pallino per regola: dal rischio delle sue porte nell'ultimo audit.
        private void AnnotateRules(List<PortAuditRow> audit)
        {
            var byPort = new Dictionary<int, PortAuditRow>();
            foreach (var a in audit) byPort[a.Port] = a;

            foreach (var rule in _rules)
            {
                var ports = ParsePorts(rule.LocalPort);
                var hits = ports.Where(byPort.ContainsKey).Select(p => byPort[p]).ToList();
                if (hits.Count == 0)
                {
                    rule.Health = "UNKNOWN";
                    rule.HealthTip = rule.LocalPort == "Any"
                        ? "Porta 'Any': esito non valutabile per singola porta."
                        : "Nessuna porta in ascolto per questa regola (non valutabile).";
                    continue;
                }
                bool anyRisk = hits.Any(h => h.Risk == "RISCHIO");
                bool anyWarn = hits.Any(h => h.Risk == "ATTENZIONE");
                rule.Health = anyRisk ? "RISK" : anyWarn ? "WARN" : "OK";
                rule.HealthTip = string.Join("\n", hits.Select(h => $"{h.Port}: {h.Exposure} — {h.Risk}"
                    + (string.IsNullOrEmpty(h.Note) ? "" : " (" + h.Note + ")")));
            }
        }

        private static IEnumerable<int> ParsePorts(string localPort)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(localPort) || localPort.Equals("Any", StringComparison.OrdinalIgnoreCase))
                return result;
            foreach (var tok in localPort.Split(','))
            {
                var t = tok.Trim();
                if (t.Contains("-"))
                {
                    var mm = t.Split('-');
                    if (mm.Length == 2 && int.TryParse(mm[0].Trim(), out int a) && int.TryParse(mm[1].Trim(), out int b))
                        for (int i = a; i <= b && i - a < 2000; i++) result.Add(i);
                }
                else if (int.TryParse(t, out int p)) result.Add(p);
            }
            return result;
        }

        private void WriteAuditLog(List<PortAuditRow> rows, bool fwOn, bool sw)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(L.FwAuditLogHeader(fwOn, sw));
                foreach (var r in rows)
                    sb.AppendLine($"  {r.Port,-6} {r.Process,-16} {r.Exposure,-9} {r.Risk,-10} {r.Purpose}"
                        + (string.IsNullOrEmpty(r.Note) ? "" : "  — " + r.Note));
                int risk = rows.Count(r => r.Risk == "RISCHIO");
                int warn = rows.Count(r => r.Risk == "ATTENZIONE");
                int ok = rows.Count(r => r.Risk == "OK");
                sb.AppendLine($"  {L.FwAuditCounts(risk, warn, ok)}");
                File.AppendAllText(_auditLogPath, sb.ToString(), Encoding.UTF8);
                Log("Test di rete → log: " + _auditLogPath);
            }
            catch (Exception ex) { Log("ERRORE scrittura log audit: " + ex.Message); }
        }

        // ── Log embedded ─────────────────────────────────────
        private void Log(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Log(msg))); return; }
            TbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TbLog.ScrollToEnd();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TbLog.Clear();
    }
}
