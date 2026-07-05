using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShaiyaServerHub.Core;
using ShaiyaServerHub.Core.WebServer;

namespace ShaiyaServerHub
{
    public partial class MainWindow : Window
    {
        private HubSettings _settings;
        private ScriptRunner _runner;
        private IWebServer _webServer;
        private DispatcherTimer _svcTimer;

        public MainWindow()
        {
            InitializeComponent();
            _settings = HubSettings.Load();
            _runner = new ScriptRunner(_settings);
            _webServer = WebServerFactory.Create(_settings);

            if (_settings.Language == "En") L.Switch(L.Lang.En);
            UpdateLangButtons();
            L.Changed += UpdateLangButtons;

            Log(L.Startup);
            ReloadScripts();
            ReloadTasks();
            RefreshServices();

            // Form "Nuovo task" riutilizzabile: log verso la UI + ricarica su creazione.
            NewTaskCtrl.LogSink = Log;
            NewTaskCtrl.Created += () => { ReloadTasks(); SelectTasks(); };

            // Aree riutilizzabili (cartelle proprie functions\ e testsuite\).
            FuncArea.AreaTitle = "PSM_Cmd-SCC";
            // Sezione Funzioni: SEMPRE Windows auth (-E → SYSTEM). L'Hub è localhost sul
            // server; così la verifica/installazione PSM_Cmd vede gli oggetti least-priv
            // (dbo.Command CLR, Gm* tabelle) invisibili all'account SQL principale
            // (es. ShaiyaTaskAgent). Le impostazioni SQL principali restano invariate.
            FuncArea.Runner = new ScriptRunner(_settings, forceWindowsAuth: true);
            FuncArea.ShowNewFile = false;   // scheda PSM_Cmd-SCC: "Nuovo file" non serve
            FuncArea.Init(ScriptRepository.Function("psm_cmd-scc"), L.HintFunctions);
            FwArea.Init(_settings);   // scheda Windows Firewall
            TestArea.AreaTitle = "Test Suite";
            TestArea.Runner = _runner;
            TestArea.Init(ScriptRepository.TestSuite, L.HintTestSuite);

            ShowView("dashboard");

            // Auto-refresh servizi ogni 3s (solo mentre la vista Servizi/Web/Dashboard è aperta).
            // Le sonde lente (web/firewall, PowerShell) NON girano nel timer: solo dati veloci.
            _svcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _svcTimer.Tick += (s, e) =>
            {
                if (PanelServices.Visibility == Visibility.Visible) RefreshServices();
                if (PanelWeb.Visibility == Visibility.Visible) RefreshWebServices();
                if (PanelDashboard.Visibility == Visibility.Visible) RefreshDashboardFast();
            };
            _svcTimer.Start();
        }

        // ── Navigazione viste ─────────────────────────────────
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            var rb = sender as RadioButton;
            ShowView(rb?.Tag as string);
        }

        private void ShowView(string view)
        {
            // I pannelli non esistono ancora durante InitializeComponent.
            if (PanelScripts == null) return;
            PanelDashboard.Visibility = view == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
            PanelScripts.Visibility = view == "scripts" ? Visibility.Visible : Visibility.Collapsed;
            PanelNewTask.Visibility = view == "newtask" ? Visibility.Visible : Visibility.Collapsed;
            PanelTasks.Visibility = view == "tasks" ? Visibility.Visible : Visibility.Collapsed;
            PanelServices.Visibility = view == "services" ? Visibility.Visible : Visibility.Collapsed;
            PanelWeb.Visibility = view == "web" ? Visibility.Visible : Visibility.Collapsed;
            PanelFunctions.Visibility = view == "functions" ? Visibility.Visible : Visibility.Collapsed;
            PanelTestSuite.Visibility = view == "testsuite" ? Visibility.Visible : Visibility.Collapsed;
            PanelLog.Visibility = view == "log" ? Visibility.Visible : Visibility.Collapsed;

            // Entrando nella vista Web: refresh completo (servizi + sonda health/cert).
            if (view == "web") RefreshWeb();
            // Entrando nella Dashboard: panoramica completa (dati veloci + sonde async).
            if (view == "dashboard") RefreshDashboard();
        }

        private void SelectLog() { if (NavLog != null) NavLog.IsChecked = true; }
        private void SelectTasks() { if (NavTasks != null) NavTasks.IsChecked = true; }

        /// <summary>Naviga a una vista dal tag (usato dalle tessere/azioni della Dashboard).</summary>
        private void SelectView(string view)
        {
            switch (view)
            {
                case "dashboard": NavDashboard.IsChecked = true; break;
                case "scripts": NavScripts.IsChecked = true; break;
                case "newtask": NavNewTask.IsChecked = true; break;
                case "tasks": NavTasks.IsChecked = true; break;
                case "services": NavServices.IsChecked = true; break;
                case "web": NavWeb.IsChecked = true; break;
                case "functions": NavFunctions.IsChecked = true; break;
                case "testsuite": NavTestSuite.IsChecked = true; break;
                case "log": NavLog.IsChecked = true; break;
            }
        }

        // ── Dashboard ─────────────────────────────────────────
        private void BtnDashRefresh_Click(object sender, RoutedEventArgs e) => RefreshDashboard(true);
        private void DashTile_Click(object sender, MouseButtonEventArgs e) =>
            SelectView((sender as FrameworkElement)?.Tag as string);
        private void DashOpen_Click(object sender, RoutedEventArgs e) =>
            SelectView((sender as FrameworkElement)?.Tag as string);

        // Throttle delle parti pesanti: entrare nella Dashboard NON deve rifare schtasks/probe
        // ogni volta. I TextBlock mantengono l'ultimo valore → riapertura istantanea; il
        // ricalcolo parte solo se i dati sono più vecchi del TTL (o su Aggiorna manuale).
        private static readonly TimeSpan _dashHeavyTtl = TimeSpan.FromSeconds(20);
        private DateTime _dashHeavyAt = DateTime.MinValue;

        private void RefreshDashboard() => RefreshDashboard(false);

        /// <summary>Panoramica: dati veloci (sync, cheap) + pesanti (off-thread, throttled).</summary>
        private void RefreshDashboard(bool force)
        {
            if (TbKpiSvc == null) return;   // pannello non ancora costruito
            TbDashHost.Text = $"{_settings.SqlServer} · {_settings.DefaultDatabase}";

            RefreshDashboardFast();   // servizi + orario: leggero, resta sincrono

            // Pesante (schtasks + discover + probe web/firewall): salta se già fresco.
            if (!force && (DateTime.Now - _dashHeavyAt) < _dashHeavyTtl) return;
            _dashHeavyAt = DateTime.Now;

            RefreshDashboardHeavyAsync();   // task + script fuori dal thread UI
            RefreshDashboardWebAsync();
            RefreshDashboardFirewallAsync();
            RefreshDashboardPsmAsync();
        }

        /// <summary>Task (schtasks) + script (filesystem) calcolati off-thread, poi in UI.</summary>
        private async void RefreshDashboardHeavyAsync()
        {
            try
            {
                var res = await Task.Run(() =>
                {
                    var tasks = TaskSchedulerService.List();
                    foreach (var t in tasks) t.LogState = TaskLogReader.LastRunState(t.Name);
                    var scripts = ScriptRepository.Main.Discover();
                    int enabled = tasks.Count(t => !t.IsDisabled);
                    int errors = tasks.Count(t => t.LogState == TaskLogState.Error);
                    int sql = scripts.Count(s => s.Kind == ScriptKind.Sql);
                    return new { Total = tasks.Count, enabled, errors, ScriptTotal = scripts.Count, sql };
                });
                if (TbKpiTasks == null) return;

                TbKpiTasks.Text = res.Total.ToString();
                TbKpiTasksSub.Text = L.DashTasksSub(res.enabled, res.errors);
                TbKpiTasksSub.Foreground = new SolidColorBrush(res.errors > 0
                    ? Color.FromRgb(0xF4, 0x6A, 0x6A) : Color.FromArgb(0x88, 0xE0, 0xE0, 0xE0));

                TbKpiScripts.Text = res.ScriptTotal.ToString();
                TbKpiScriptsSub.Text = L.DashScriptsSub(res.sql, res.ScriptTotal - res.sql);
            }
            catch (Exception ex) { App.WriteLog("DASH-HEAVY", ex.ToString()); }
        }

        /// <summary>Solo dati veloci (servizi + orario), adatti al timer 3s.</summary>
        private void RefreshDashboardFast()
        {
            if (DashServicesList == null) return;
            var svc = ServiceMonitor.Query();
            DashServicesList.ItemsSource = svc;
            int online = svc.Count(s => s.Running);
            int total = svc.Count(s => s.Installed);
            if (total == 0) total = svc.Count;
            TbKpiSvc.Text = $"{online}/{total}";
            TbKpiSvc.Foreground = new SolidColorBrush(
                online == 0 ? Color.FromRgb(0xF4, 0x6A, 0x6A) :
                online >= total ? Color.FromRgb(0x4D, 0xC8, 0x70) :
                Color.FromRgb(0xE8, 0xC8, 0x81));
            TbKpiSvcSub.Text = L.DashSvcSub(online, total);
            TbDashUpdated.Text = L.DashUpdated(DateTime.Now);
        }

        private async void RefreshDashboardWebAsync()
        {
            if (_webServer == null) _webServer = WebServerFactory.Create(_settings);
            TbKpiWeb.Text = L.DashWebProbing;
            TbKpiWeb.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x81));
            TbDashWebKind.Text = _webServer.DisplayName;
            TbDashWebHealth.Text = L.DashWebProbing;
            TbDashWebCert.Text = "";

            var res = await Task.Run(() => WebProbe.Probe(_webServer));
            if (TbKpiWeb == null) return;

            TbKpiWeb.Text = res.Reachable ? L.DashWebOnline : L.DashWebOffline;
            var webColor = !res.Reachable ? Color.FromRgb(0xF4, 0x6A, 0x6A)
                         : res.AppServer ? Color.FromRgb(0x4D, 0xC8, 0x70)
                                         : Color.FromRgb(0xE8, 0xC8, 0x81);
            TbKpiWeb.Foreground = new SolidColorBrush(webColor);
            TbDashWebHealth.Text = res.Summary();
            TbDashWebHealth.Foreground = new SolidColorBrush(webColor);

            if (res.CertExpiry.HasValue)
            {
                int days = (int)(res.CertExpiry.Value.Date - DateTime.Now.Date).TotalDays;
                TbDashWebCert.Text = L.WebCert(res.CertExpiry.Value, days);
                TbDashWebCert.Foreground = new SolidColorBrush(days <= 14
                    ? Color.FromRgb(0xF4, 0x6A, 0x6A) : Color.FromArgb(0x66, 0xE0, 0xE0, 0xE0));
                TbKpiWebSub.Text = L.WebCert(res.CertExpiry.Value, days);
            }
            else
            {
                TbDashWebCert.Text = L.WebCertNone;
                TbDashWebCert.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x7A));
                TbKpiWebSub.Text = _webServer.DisplayName;
            }
        }

        private async void RefreshDashboardFirewallAsync()
        {
            TbDashFwState.Text = L.DashFwProbing;
            TbDashFwState.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x81));
            TbDashFwSub.Text = "";

            FirewallStatus st = null;
            await RunAsync(() => st = new FirewallManager().GetStatus());
            if (TbDashFwState == null) return;

            if (st == null || st.Profiles.Count == 0)
            {
                TbDashFwState.Text = L.DashFwUnknown;
                TbDashFwState.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x7A));
                return;
            }

            int on = st.Profiles.Count(p => p.Enabled);
            int total = st.Profiles.Count;
            if (st.AllEnabled)
            {
                TbDashFwState.Text = L.DashFwOn;
                TbDashFwState.Foreground = new SolidColorBrush(Color.FromRgb(0x4D, 0xC8, 0x70));
            }
            else if (st.AnyEnabled)
            {
                TbDashFwState.Text = L.DashFwPartial;
                TbDashFwState.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB0, 0x40));
            }
            else
            {
                TbDashFwState.Text = L.DashFwOff;
                TbDashFwState.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x6A, 0x6A));
            }
            TbDashFwSub.Text = L.DashFwSub(on, total, st.SimplewallFiltering);
        }

        private async void RefreshDashboardPsmAsync()
        {
            TbDashPsmState.Text = L.DashPsmProbing;
            TbDashPsmState.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x81));
            TbDashPsmSub.Text = L.DashPsmSub;

            bool? ok = null;
            // Come la scheda Funzioni: sempre Windows auth (-E → SYSTEM) per vedere gli oggetti
            // least-priv di PSM_Cmd (dbo.Command CLR, Gm* tabelle) invisibili all'account SQL principale.
            await RunAsync(() =>
            {
                var runner = new ScriptRunner(_settings, forceWindowsAuth: true);
                ok = CommandChannel.VerifyPsmCmd(runner, _ => { });
            });
            if (TbDashPsmState == null) return;

            if (ok == true)
            {
                TbDashPsmState.Text = L.DashPsmOn;
                TbDashPsmState.Foreground = new SolidColorBrush(Color.FromRgb(0x4D, 0xC8, 0x70));
            }
            else if (ok == false)
            {
                TbDashPsmState.Text = L.DashPsmOff;
                TbDashPsmState.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x6A, 0x6A));
            }
            else
            {
                TbDashPsmState.Text = L.DashPsmUnknown;
                TbDashPsmState.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x7A));
            }
        }

        // ── Custom chrome ─────────────────────────────────────
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
        private void BtnMin_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, MouseButtonEventArgs e) => Close();

        // Grip angolo basso-destra: ridimensiona la finestra rispettando i minimi.
        private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double w = Width + e.HorizontalChange;
            double h = Height + e.VerticalChange;
            if (w >= MinWidth) Width = w;
            if (h >= MinHeight) Height = h;
        }

        private static readonly SolidColorBrush _hover = new SolidColorBrush(Color.FromArgb(0x22, 0xE8, 0xC8, 0x81));
        private static readonly SolidColorBrush _closeHover = new SolidColorBrush(Color.FromArgb(0x30, 0xF4, 0x6A, 0x6A));
        private static readonly SolidColorBrush _transp = new SolidColorBrush(Colors.Transparent);
        private void BtnChromeBtn_MouseEnter(object sender, MouseEventArgs e) => ((Border)sender).Background = _hover;
        private void BtnChromeBtn_MouseLeave(object sender, MouseEventArgs e) => ((Border)sender).Background = _transp;
        private void BtnCloseChromeBtn_MouseEnter(object sender, MouseEventArgs e) => ((Border)sender).Background = _closeHover;

        // ── Language ──────────────────────────────────────────
        private void BtnLangIt_Click(object sender, MouseButtonEventArgs e) => SwitchLang(L.Lang.It);
        private void BtnLangEn_Click(object sender, MouseButtonEventArgs e) => SwitchLang(L.Lang.En);
        private void SwitchLang(L.Lang lang)
        {
            L.Switch(lang);
            _settings.Language = lang == L.Lang.En ? "En" : "It";
            _settings.Save();
        }
        private void UpdateLangButtons()
        {
            bool it = L.Current == L.Lang.It;
            BtnLangIt.Background = it ? new SolidColorBrush(Color.FromArgb(0x20, 0xE8, 0xC8, 0x81)) : Brushes.Transparent;
            BtnLangIt.BorderBrush = it ? new SolidColorBrush(Color.FromArgb(0x7B, 0xE8, 0xC8, 0x81)) : new SolidColorBrush(Color.FromArgb(0x15, 0xE8, 0xC8, 0x81));
            TbLangIt.Foreground = it ? new SolidColorBrush(Color.FromArgb(0xFF, 0xE8, 0xC8, 0x81)) : new SolidColorBrush(Color.FromArgb(0x35, 0xE8, 0xC8, 0x81));
            BtnLangEn.Background = !it ? new SolidColorBrush(Color.FromArgb(0x20, 0xE8, 0xC8, 0x81)) : Brushes.Transparent;
            BtnLangEn.BorderBrush = !it ? new SolidColorBrush(Color.FromArgb(0x7B, 0xE8, 0xC8, 0x81)) : new SolidColorBrush(Color.FromArgb(0x15, 0xE8, 0xC8, 0x81));
            TbLangEn.Foreground = !it ? new SolidColorBrush(Color.FromArgb(0xFF, 0xE8, 0xC8, 0x81)) : new SolidColorBrush(Color.FromArgb(0x35, 0xE8, 0xC8, 0x81));
        }

        // ── Settings ──────────────────────────────────────────
        private void BtnSettings_Click(object sender, MouseButtonEventArgs e)
        {
            var win = new SettingsWindow(_settings, _runner, Log) { Owner = this };
            if (win.ShowDialog() == true)
            {
                _settings.Save();
                _runner = new ScriptRunner(_settings);
                _webServer = WebServerFactory.Create(_settings);
                FuncArea.Runner = new ScriptRunner(_settings, forceWindowsAuth: true); // Funzioni: sempre -E
                TestArea.Runner = _runner;
                Log(L.SettingsSaved);
            }
        }

        // ── Funzioni: PSM_Cmd ─────────────────────────────────
        private async void BtnVerifyPsmCmd_Click(object sender, RoutedEventArgs e)
        {
            FuncArea.Log("── Verifica PSM_Cmd ──");
            await FuncArea.RunAsync(() =>
            {
                bool ok = CommandChannel.VerifyPsmCmd(FuncArea.Runner, FuncArea.Log);
                FuncArea.Log(ok
                    ? "✓ PSM_Cmd ATTIVO e funzionante."
                    : "✗ PSM_Cmd NON attivo/incompleto → usa 'Installa/Ripara' (metti gli script 00→07 in functions\\psm_cmd-scc\\sql).");
            });
        }

        private async void BtnInstallPsmCmd_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(L.ConfirmInstallPsm, "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            FuncArea.Log("── Installa/Ripara PSM_Cmd ──");
            FuncArea.Log("Prerequisiti: PSMagent.signed.dll in C:\\ShaiyaServer\\PSM_Client, login sysadmin, password aggiornate in 01/04.");
            await FuncArea.RunAllSqlAsync();
            FuncArea.Log("Suggerito: ora premi 'Verifica PSM_Cmd' per confermare.");
        }

        private void BtnPsmCmdSettings_Click(object sender, RoutedEventArgs e)
        {
            try { new PsmCmdSettingsWindow(_settings) { Owner = this }.ShowDialog(); }
            catch (Exception ex) { FuncArea.Log("ERRORE apertura impostazioni PSM_Cmd: " + ex.Message); }
        }

        private void BtnPsmCmdLog_Click(object sender, RoutedEventArgs e)
        {
            try { new PsmCmdLogWindow(_settings) { Owner = this }.ShowDialog(); }
            catch (Exception ex) { FuncArea.Log("ERRORE apertura log PSM_Cmd: " + ex.Message); }
        }

        // ── Test Suite ────────────────────────────────────────
        private async void BtnRunTestSuite_Click(object sender, RoutedEventArgs e)
        {
            TestArea.Log("── Esecuzione test suite (SQL + PS1/BAT) ──");
            await TestArea.RunAllAsync();
        }

        // ── Scripts ───────────────────────────────────────────
        private void BtnReloadScripts_Click(object sender, RoutedEventArgs e) => ReloadScripts();
        private void ReloadScripts()
        {
            var repo = ScriptRepository.Main;
            var list = repo.Discover();
            ScriptsSqlList.ItemsSource = list.Where(s => s.Kind == ScriptKind.Sql).ToList();
            ScriptsPsList.ItemsSource = list.Where(s => s.Kind != ScriptKind.Sql).ToList();
            Log(L.ScriptsFound(list.Count));
        }

        private void BtnOpenFolders_Click(object sender, RoutedEventArgs e)
        {
            var repo = ScriptRepository.Main;
            repo.EnsureFolders();
            try { Process.Start("explorer.exe", repo.SqlDir); } catch { }
            try { Process.Start("explorer.exe", repo.Ps1Dir); } catch { }
        }

        private async void BtnRunScript_Click(object sender, RoutedEventArgs e)
        {
            var item = ((FrameworkElement)sender).DataContext as ScriptItem;
            if (item == null) return;
            SelectLog();
            Log(L.RunningScript(item.Name));
            await RunAsync(() => _runner.RunScript(item, Log));
        }

        private void BtnScheduleScript_Click(object sender, RoutedEventArgs e)
        {
            var item = ((FrameworkElement)sender).DataContext as ScriptItem;
            if (item == null) return;
            // Apre il modale "Nuovo task pianificato" con lo script preselezionato.
            var win = new NewTaskWindow(item.FullPath, Log) { Owner = this };
            if (win.ShowDialog() == true)
            {
                ReloadTasks();
                SelectTasks();
            }
        }

        // ── Editor file (modifica veloce script) ──────────────
        private void BtnEditScript_Click(object sender, RoutedEventArgs e)
        {
            var item = ((FrameworkElement)sender).DataContext as ScriptItem;
            if (item == null) return;
            if (OpenEditor(item.FullPath)) ReloadScripts();
        }

        private void BtnNewScript_Click(object sender, RoutedEventArgs e)
        {
            var repo = ScriptRepository.Main;
            if (CreateAndEditNewFile(repo)) ReloadScripts();
        }

        /// <summary>Apre l'editor su un file; ritorna true se è stato salvato.</summary>
        private bool OpenEditor(string fullPath)
        {
            var win = new EditorWindow(fullPath) { Owner = this };
            return win.ShowDialog() == true;
        }

        /// <summary>SaveFileDialog nella cartella dell'area → crea il file vuoto → apre l'editor.</summary>
        private bool CreateAndEditNewFile(ScriptRepository repo)
        {
            repo.EnsureFolders();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = L.NewFilePrompt,
                InitialDirectory = repo.SqlDir,
                Filter = "SQL (*.sql)|*.sql|PowerShell (*.ps1)|*.ps1|Batch (*.bat)|*.bat|Tutti|*.*",
                FilterIndex = 1,
                OverwritePrompt = false
            };
            if (dlg.ShowDialog(this) != true) return false;
            string path = dlg.FileName;
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "", System.Text.Encoding.UTF8);
            }
            catch (Exception ex) { Log(L.Err(ex.Message)); return false; }
            return OpenEditor(path);
        }

        // ── Tasks ─────────────────────────────────────────────
        private void BtnReloadTasks_Click(object sender, RoutedEventArgs e) => ReloadTasks();
        private void ReloadTasks()
        {
            var tasks = TaskSchedulerService.List();
            // Colore bottone Log = esito ultimo run letto dal log per-task.
            foreach (var t in tasks) t.LogState = TaskLogReader.LastRunState(t.Name);
            TasksList.ItemsSource = tasks;
        }

        private void BtnTaskLog_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            string text = TaskLogReader.Read(t.Name, out string _);
            if (text == null) { SetTaskStatus(L.NoTaskLog(t.Name), false); return; }
            new LogWindow($"Log · {t.Name}", text) { Owner = this }.ShowDialog();
        }

        private async void BtnEnableTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            int rc = -1;
            await RunAsync(() => rc = TaskSchedulerService.SetEnabled(t.FullName, true, Log));
            SetTaskStatus(rc == 0 ? L.TaskEnabled(t.Name) : L.TaskActionErr(t.Name, rc), rc == 0);
            ReloadTasks();
        }
        private async void BtnDisableTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            int rc = -1;
            await RunAsync(() => rc = TaskSchedulerService.SetEnabled(t.FullName, false, Log));
            SetTaskStatus(rc == 0 ? L.TaskDisabled(t.Name) : L.TaskActionErr(t.Name, rc), rc == 0);
            ReloadTasks();
        }
        private async void BtnRunNowTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            DateTime before = TaskLogReader.LastWrite(t.Name);   // mtime log prima del run
            int rc = -1;
            await RunAsync(() => rc = TaskSchedulerService.RunNow(t.FullName, Log));
            SetTaskStatus(rc == 0 ? L.TaskRun(t.Name) : L.TaskActionErr(t.Name, rc), rc == 0);
            if (rc != 0) return;
            // schtasks /Run è async: attendo che il log si aggiorni (max ~8s), poi ricarico → colore fresco.
            await Task.Run(() =>
            {
                for (int i = 0; i < 32; i++)
                {
                    if (TaskLogReader.LastWrite(t.Name) > before) break;
                    System.Threading.Thread.Sleep(250);
                }
            });
            ReloadTasks();
        }
        private async void BtnRemoveTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            if (MessageBox.Show(L.ConfirmRemoveTask(t.Name), "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int rc = -1;
            await RunAsync(() => rc = TaskSchedulerService.Delete(t.FullName, Log));
            SetTaskStatus(rc == 0 ? L.TaskRemoved(t.Name) : L.TaskActionErr(t.Name, rc), rc == 0);
            ReloadTasks();
        }

        private async void BtnEditTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            string xml = null;
            await RunAsync(() => xml = TaskSchedulerService.GetXml(t.FullName));
            if (string.IsNullOrWhiteSpace(xml)) { SetTaskStatus(L.TaskActionErr(t.Name, -1), false); return; }
            var def = TaskXml.Parse(xml, t.Name);
            var win = new NewTaskWindow(def, Log) { Owner = this };
            if (win.ShowDialog() == true)
            {
                SetTaskStatus(L.TaskUpdated(t.Name), true);
                ReloadTasks();
            }
        }

        private void BtnOpenTaskSched_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("taskschd.msc"); }
            catch (Exception ex) { Log(L.Err(ex.Message)); }
        }

        // Messaggio esito operazioni task, mostrato in cima a Pianificati (verde/rosso).
        private void SetTaskStatus(string text, bool ok)
        {
            TbTaskStatus.Text = text;
            TbTaskStatus.Foreground = new SolidColorBrush(ok
                ? Color.FromRgb(0x4D, 0xC8, 0x70) : Color.FromRgb(0xF4, 0x6A, 0x6A));
        }

        // ── Services ──────────────────────────────────────────
        private void BtnRefreshServices_Click(object sender, RoutedEventArgs e) => RefreshServices();
        private void RefreshServices() => ServicesList.ItemsSource = ServiceMonitor.Query();

        private async void BtnStartServices_Click(object sender, RoutedEventArgs e)
        {
            SelectLog();
            await RunAsync(() => ServiceMonitor.StartAll(Log));
            RefreshServices();
        }

        private async void BtnStartService_Click(object sender, RoutedEventArgs e)
        {
            var s = ((FrameworkElement)sender).DataContext as ServiceStatus;
            if (s == null) return;
            SelectLog();
            await RunAsync(() => ServiceMonitor.Start(s.ServiceName, Log));
            RefreshServices();
        }

        private async void BtnStopService_Click(object sender, RoutedEventArgs e)
        {
            var s = ((FrameworkElement)sender).DataContext as ServiceStatus;
            if (s == null) return;
            if (MessageBox.Show(L.ConfirmStop(s.ServiceName), "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunAsync(() => ServiceMonitor.Stop(s.ServiceName, Log));
            RefreshServices();
            RefreshWebServices();   // il servizio potrebbe essere web (Apache/mysql)
        }

        // ── Web server (XAMPP/Apache · IIS) ───────────────────
        private void RefreshWebServices()
        {
            if (_webServer == null) return;
            WebServicesList.ItemsSource = ServiceMonitor.Query(_webServer.Services);
        }

        private async void RefreshWeb()
        {
            if (WebServicesList == null) return;                 // pannello non ancora costruito
            if (_webServer == null) _webServer = WebServerFactory.Create(_settings);
            TbWebKind.Text = L.WebKind(_webServer.DisplayName, _webServer.WebRoot);
            RefreshWebServices();

            TbWebHealth.Text = L.WebProbing;
            SetWebHealth(0xE8, 0xC8, 0x81);
            var res = await Task.Run(() => WebProbe.Probe(_webServer));
            TbWebHealth.Text = res.Summary();
            if (!res.Reachable) SetWebHealth(0xF4, 0x6A, 0x6A);
            else if (res.AppServer) SetWebHealth(0x4D, 0xC8, 0x70);
            else SetWebHealth(0xE8, 0xC8, 0x81);

            if (res.CertExpiry.HasValue)
            {
                int days = (int)(res.CertExpiry.Value.Date - DateTime.Now.Date).TotalDays;
                TbWebCert.Text = L.WebCert(res.CertExpiry.Value, days);
                TbWebCert.Foreground = new SolidColorBrush(days <= 14
                    ? Color.FromRgb(0xF4, 0x6A, 0x6A) : Color.FromArgb(0x66, 0xE0, 0xE0, 0xE0));
            }
            else { TbWebCert.Text = L.WebCertNone; TbWebCert.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x7A)); }
        }

        private void SetWebHealth(byte r, byte g, byte b) =>
            TbWebHealth.Foreground = new SolidColorBrush(Color.FromRgb(r, g, b));

        private void BtnRefreshWeb_Click(object sender, RoutedEventArgs e) => RefreshWeb();

        private async void BtnRestartWeb_Click(object sender, RoutedEventArgs e)
        {
            if (_webServer == null) return;
            string svc = _webServer.PrimaryService;
            if (MessageBox.Show(L.ConfirmRestartWeb(svc), "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SelectLog();
            await RunAsync(() =>
            {
                ServiceMonitor.Stop(svc, Log);
                ServiceMonitor.Start(svc, Log);
            });
            RefreshWebServices();
        }

        private async void BtnWebSslGen_Click(object sender, RoutedEventArgs e)
        {
            if (_webServer == null) return;
            string path = FindMainScript(_webServer.SslCertScript);
            if (path == null) { Log(L.WebScriptMissing(_webServer.SslCertScript)); SelectLog(); return; }
            if (MessageBox.Show(L.ConfirmWebSslGen(_webServer.SslCertScript), "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SelectLog();
            Log(L.RunningScript(System.IO.Path.GetFileName(path)));
            await RunAsync(() => _runner.RunPowerShell(path, Log));
        }

        private async void BtnBackupWeb_Click(object sender, RoutedEventArgs e)
        {
            string path = FindMainScript("BackupFolderWEB.bat");
            if (path == null) { Log(L.WebScriptMissing("BackupFolderWEB.bat")); SelectLog(); return; }
            SelectLog();
            Log(L.RunningScript(System.IO.Path.GetFileName(path)));
            await RunAsync(() => _runner.RunBatch(path, Log));
        }

        private void BtnOpenWebRoot_Click(object sender, RoutedEventArgs e)
        {
            try { if (_webServer != null) Process.Start("explorer.exe", _webServer.WebRoot); }
            catch (Exception ex) { Log(L.Err(ex.Message)); }
        }

        private void BtnOpenWebLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_webServer == null || _webServer.LogPaths.Length == 0) return;
                string dir = System.IO.Path.GetDirectoryName(_webServer.LogPaths[0]);
                if (System.IO.Directory.Exists(dir)) Process.Start("explorer.exe", dir);
                else Log(L.WebLogsMissing(dir));
            }
            catch (Exception ex) { Log(L.Err(ex.Message)); }
        }

        private void BtnWebSettings_Click(object sender, RoutedEventArgs e)
        {
            new WebSettingsWindow(_settings, Log) { Owner = this }.ShowDialog();
        }

        /// <summary>Cerca uno script per nome file nell'area principale (scripts\ps1, ricorsivo).</summary>
        private static string FindMainScript(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            try
            {
                var dir = ScriptRepository.Main.Ps1Dir;
                return System.IO.Directory.EnumerateFiles(dir, fileName, System.IO.SearchOption.AllDirectories)
                                          .FirstOrDefault();
            }
            catch { return null; }
        }

        // ── Log & helpers ─────────────────────────────────────
        private void Log(string msg)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Log(msg))); return; }
            TbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TbLog.ScrollToEnd();
            // hub.log resta pulito: NON logghiamo ogni riga UI qui.
            // Solo avvio (App) ed errori (RunAsync → App.WriteLog EXCEPTION) finiscono in hub.log.
        }
        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TbLog.Clear();

        private async Task RunAsync(Action action)
        {
            try { await Task.Run(action); }
            catch (Exception ex)
            {
                App.WriteLog("EXCEPTION", ex.ToString());
                Log(L.Err(ex.Message));
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _svcTimer?.Stop();
            L.Changed -= UpdateLangButtons;
            base.OnClosed(e);
        }
    }
}
