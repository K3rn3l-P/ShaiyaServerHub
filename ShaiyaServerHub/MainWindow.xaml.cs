using System;
using System.Collections.Generic;
using System.Data.SqlClient;
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
        private DispatcherTimer _headerStatusTimer;

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
            // Test Suite: SEMPRE Windows auth (-E → SYSTEM), come la scheda Funzioni.
            // I test di sicurezza toccano DB (PS_UserData ecc.) invisibili all'account SQL
            // principale (es. ShaiyaTaskAgent, limitato a PSM_Cmd). Impostazioni SQL invariate.
            TestArea.Runner = new ScriptRunner(_settings, forceWindowsAuth: true);
            TestArea.Init(ScriptRepository.TestSuite, L.HintTestSuite);
            RefreshExternalToolsStatus();
            KeyRotArea.Init(_settings);   // vista Key Rotation (rotazione chiavi)
            AiArea.Init(_settings);       // vista AI (Hub Brain, P1: chat read-only)

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

            // Timer di stato header + ri-kick manutenzione: gira SEMPRE (non solo a Dashboard aperta,
            // a differenza delle altre sonde) — pilota la barra persistente nell'header (visibile in
            // OGNI vista: banner manutenzione, player online, salute sistema) e il ri-kick automatico
            // (copre il caso di un player fermo al Character Screen che /kickuid non "vede" finché non
            // rientra in game). Vedi HeaderStatusTick.
            _headerStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _headerStatusTimer.Tick += (s, e) => HeaderStatusTick();
            _headerStatusTimer.Start();
            HeaderStatusTick();   // primo giro subito, non aspettare 15s per popolare la barra
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
            PanelKeyRotation.Visibility = view == "keyrotation" ? Visibility.Visible : Visibility.Collapsed;
            PanelAi.Visibility = view == "ai" ? Visibility.Visible : Visibility.Collapsed;
            PanelLog.Visibility = view == "log" ? Visibility.Visible : Visibility.Collapsed;

            // Entrando nella vista Web: refresh completo (servizi + sonda health/cert).
            if (view == "web") RefreshWeb();
            // Entrando nella Dashboard: panoramica completa (dati veloci + sonde async).
            if (view == "dashboard") RefreshDashboard();
            // Entrando in Key Rotation: aggiorna stato pianificazione + fingerprint chiavi.
            if (view == "keyrotation") KeyRotArea.RefreshAll();
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
                case "keyrotation": NavKeyRotation.IsChecked = true; break;
                case "ai": NavAi.IsChecked = true; break;
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
            RefreshDashboardKeyRotAsync();
            RefreshDashboardMaintenanceAsync();
            RefreshDashboardAiAsync();
        }

        /// <summary>Card AI: cosa è configurato (P1 non ha ancora un "verdetto" di salute — arriva in P2 con ActionQueue).</summary>
        private async void RefreshDashboardAiAsync()
        {
            if (TbDashAiState == null) return;
            var rep = await Task.Run(() => Core.Ai.AiReport.Load(_settings));
            if (TbDashAiState == null) return;
            ApplyAiCard(rep);
        }

        private void ApplyAiCard(Core.Ai.AiReport rep)
        {
            if (TbDashAiState == null || rep == null) return;

            Color dot; string state;
            if (!rep.MasterEnabled)
            {
                dot = Color.FromRgb(0x66, 0x70, 0x7A);
                state = L.DashAiOff;
            }
            else if (!rep.ApiKeyConfigured)
            {
                dot = Color.FromRgb(0xE8, 0xB0, 0x40);
                state = L.DashAiNotConfigured;
            }
            else
            {
                dot = Color.FromRgb(0x4D, 0xC8, 0x70);
                state = L.DashAiOn;
            }
            DashAiDot.Fill = new SolidColorBrush(dot);
            TbDashAiState.Text = state;
            TbDashAiState.Foreground = new SolidColorBrush(dot);
            TbDashAiSub.Text = L.DashAiSub(rep.ToolCount, rep.SqlReaderConfigured, rep.TelegramConfigured);
        }

        /// <summary>Card Key Rotation: stato task 4:00 + esito ultimo run + data ultima rotazione (off-thread).</summary>
        private async void RefreshDashboardKeyRotAsync()
        {
            if (TbDashKrState == null) return;
            TbDashKrState.Text = L.KrChecking;
            TbDashKrState.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x81));
            TbDashKrSub.Text = "";

            string iniPath = System.IO.Path.Combine(_settings.ServerBinPath ?? "", "Data", "GMSecurity.ini");
            var rep = await Task.Run(() => KeyRotationReport.Load(iniPath));
            if (TbDashKrState == null) return;
            ApplyKeyRotationCard(rep);
        }

        private void ApplyKeyRotationCard(KeyRotationReport rep)
        {
            if (TbDashKrState == null || rep == null) return;

            Color dot; string state;
            if (!rep.TaskInstalled)
            {
                dot = Color.FromRgb(0x66, 0x70, 0x7A);
                state = L.DashKrNotInstalled;
            }
            else if (rep.LastRun == TaskLogState.Error)
            {
                dot = Color.FromRgb(0xF4, 0x6A, 0x6A);
                state = L.DashKrError;
            }
            else if (!rep.TaskEnabled)
            {
                dot = Color.FromRgb(0xE8, 0xB0, 0x40);
                state = L.DashKrDisabled;
            }
            else
            {
                dot = Color.FromRgb(0x4D, 0xC8, 0x70);
                state = L.DashKrOn;
            }
            DashKrDot.Fill = new SolidColorBrush(dot);
            TbDashKrState.Text = state;
            TbDashKrState.Foreground = new SolidColorBrush(dot);
            TbDashKrSub.Text = L.DashKrSub(rep.HasIni ? rep.LastRotation : (DateTime?)null, rep.NextRun);
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
                    int okRun = tasks.Count(t => t.LogState == TaskLogState.Ok);
                    int sql = scripts.Count(s => s.Kind == ScriptKind.Sql);
                    var ts = TestSuiteReport.Load();
                    return new { Total = tasks.Count, enabled, errors, okRun,
                                 ScriptTotal = scripts.Count, sql, ts };
                });
                if (TbKpiTasks == null) return;

                // Tessera Task: numero totale + esiti ultimo run (● verde OK · ● rosso errore).
                TbKpiTasks.Text = res.Total.ToString();
                TbKpiTasksOk.Text = res.okRun.ToString();
                TbKpiTasksErr.Text = res.errors.ToString();

                TbKpiScripts.Text = res.ScriptTotal.ToString();
                TbKpiScriptsSub.Text = L.DashScriptsSub(res.sql, res.ScriptTotal - res.sql);

                ApplyTestSuiteCard(res.ts);
            }
            catch (Exception ex) { App.WriteLog("DASH-HEAVY", ex.ToString()); }
        }

        /// <summary>Card Test Suite: esiti dell'ultimo run (● PASS/WARN/FAIL) dal log dell'area.</summary>
        private void ApplyTestSuiteCard(TestSuiteReport ts)
        {
            if (TbDashTsPass == null) return;
            if (ts == null || !ts.HasData)
            {
                TbDashTsSummary.Text = L.DashTsNever;
                TbDashTsPass.Text = TbDashTsWarn.Text = TbDashTsFail.Text = "–";
                return;
            }
            TbDashTsSummary.Text = L.DashTsSummary(ts.Total, ts.When);
            TbDashTsPass.Text = ts.Pass.ToString();
            TbDashTsWarn.Text = ts.Warn.ToString();
            TbDashTsFail.Text = ts.FailTotal.ToString();
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

        /// <summary>Solo la tessera KPI "Web" in alto (la card dettagliata Web è stata sostituita
        /// dalla card Manutenzione — vedi <see cref="RefreshDashboardMaintenanceAsync"/>).</summary>
        private async void RefreshDashboardWebAsync()
        {
            if (_webServer == null) _webServer = WebServerFactory.Create(_settings);
            TbKpiWeb.Text = L.DashWebProbing;
            TbKpiWeb.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x81));

            var res = await Task.Run(() => WebProbe.Probe(_webServer));
            if (TbKpiWeb == null) return;

            TbKpiWeb.Text = res.Reachable ? L.DashWebOnline : L.DashWebOffline;
            var webColor = !res.Reachable ? Color.FromRgb(0xF4, 0x6A, 0x6A)
                         : res.AppServer ? Color.FromRgb(0x4D, 0xC8, 0x70)
                                         : Color.FromRgb(0xE8, 0xC8, 0x81);
            TbKpiWeb.Foreground = new SolidColorBrush(webColor);

            if (res.CertExpiry.HasValue)
            {
                int days = (int)(res.CertExpiry.Value.Date - DateTime.Now.Date).TotalDays;
                TbKpiWebSub.Text = L.WebCert(res.CertExpiry.Value, days);
            }
            else
            {
                TbKpiWebSub.Text = _webServer.DisplayName;
            }
        }

        // ── Card Manutenzione ────────────────────────────────
        // Stato cachato dall'ultimo Load, usato dal click del toggle per decidere Enter/Exit
        // senza dover rileggere il DB in modo sincrono sul thread UI.
        private bool _mntEnabled;
        private bool _mntHasTable;

        /// <summary>Card Manutenzione: stato normale/attiva da PS_UserData.dbo.MaintenanceMode (off-thread).</summary>
        private async void RefreshDashboardMaintenanceAsync()
        {
            if (TbDashMntState == null) return;
            TbDashMntState.Text = L.MntChecking;
            TbDashMntState.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x81));
            TbDashMntSub.Text = "";
            TbDashMntPlayers.Text = "";

            var rep = await Task.Run(() => MaintenanceReport.Load(_settings));
            if (TbDashMntState == null) return;
            ApplyMaintenanceCard(rep);
        }

        private void ApplyMaintenanceCard(MaintenanceReport rep)
        {
            if (TbDashMntState == null || rep == null) return;

            _mntHasTable = rep.HasTable;
            _mntEnabled = rep.Enabled;

            Color dot; string state;
            if (!rep.HasTable)
            {
                dot = Color.FromRgb(0x66, 0x70, 0x7A);
                state = L.MntNotInstalled;
            }
            else if (rep.Enabled)
            {
                dot = Color.FromRgb(0xF4, 0x6A, 0x6A);
                state = L.MntActive;
            }
            else
            {
                dot = Color.FromRgb(0x4D, 0xC8, 0x70);
                state = L.MntNormal;
            }
            DashMntDot.Fill = new SolidColorBrush(dot);
            TbDashMntState.Text = state;
            TbDashMntState.Foreground = new SolidColorBrush(dot);
            TbDashMntPlayers.Text = rep.CountsOk ? L.MntPlayers(rep.AdminOnline, rep.NormalOnline) : L.MntPlayersUnknown;
            TbDashMntSub.Text = L.MntSub(rep.HasTable, rep.ChangedAt, rep.ChangedBy);

            BtnMaintenanceToggle.Content = rep.Enabled ? L.MntBtnExit : L.MntBtnEnter;
            BtnMaintenanceToggle.IsEnabled = rep.HasTable;

            // Ri-kick manuale: ha senso solo a manutenzione attiva.
            BtnMaintenanceRekick.Visibility = rep.Enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Attiva/termina la manutenzione. Azione sensibile (kick giocatori / blocco
        /// login): conferma sempre con MessageBox. Auth SPLIT (vedi feedback utente):
        /// - scrittura tabella (UPDATE MaintenanceMode) → SOLO Windows auth (-E/system):
        ///   ShaiyaTaskAgent non ha permessi di scrittura tabelle.
        /// - comandi slash (/nt, /kickuid via usp_SendNotice/usp_RunCommand, notice-*.sql/
        ///   kick-normals.sql in ScriptRepository.Maintenance) → account configurato in
        ///   Impostazioni (default ShaiyaTaskAgent, tier ECONOMY): può SOLO eseguire i wrapper
        ///   allowlist, MAI scrivere tabelle. Stesso principio di FuncArea/TestArea (due
        ///   ScriptRunner distinti) — vedi [[funzioni-forced-windows-auth]].</summary>
        private async void BtnMaintenanceToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!_mntHasTable)
            {
                Log(L.MntMissingTable);
                SelectLog();
                return;
            }

            bool entering = !_mntEnabled;
            string confirmText = entering ? L.MntConfirmEnter : L.MntConfirmExit;
            if (MessageBox.Show(confirmText, L.MntConfirmTitle, MessageBoxButton.YesNo,
                    entering ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var repo = ScriptRepository.Maintenance;
            string noticePath = Path.Combine(repo.SqlDir, entering ? "notice-enter.sql" : "notice-exit.sql");
            if (!File.Exists(noticePath)) { Log(L.MntScriptMissing(Path.GetFileName(noticePath))); SelectLog(); return; }

            SelectLog();
            BtnMaintenanceToggle.IsEnabled = false;

            var sysRunner = new ScriptRunner(_settings, forceWindowsAuth: true);   // tabella + lettura Chars/Users_Master: sempre -E
            var taskRunner = new ScriptRunner(_settings);                          // SOLO comandi slash (usp_RunCommand/usp_SendNotice): auth configurata (ShaiyaTaskAgent)

            bool ok = true;
            await RunAsync(() =>
            {
                Log(entering ? L.MntTogglingOn : L.MntTogglingOff);
                string upd = "UPDATE PS_UserData.dbo.MaintenanceMode SET Enabled=" + (entering ? "1" : "0") +
                             ", ChangedAt=GETDATE(), ChangedBy=SUSER_SNAME();";
                if (sysRunner.RunSqlQuery(upd, out _, Log) != 0) { ok = false; return; }

                Log(L.MntSendingNotice);
                if (taskRunner.RunSql(noticePath, Log) != 0) Log(L.MntNoticeFailed);

                if (entering)
                {
                    Log(L.MntKicking);
                    if (!RunMaintenanceKickPass(verbose: true)) ok = false;
                }
            });

            Log(ok ? (entering ? L.MntEnterDone : L.MntExitDone) : L.MntActionFailed);
            RefreshDashboardMaintenanceAsync();
        }

        /// <summary>Pulsante di sicurezza sulla card: ri-kick manuale a domanda (es. un player
        /// rimasto al Character Screen non viene "visto" da /kickuid finché non rientra in game —
        /// limite noto). Visibile solo a manutenzione attiva (vedi ApplyMaintenanceCard). Vedi anche
        /// <see cref="MaintenanceAutoKickTick"/> (stesso ri-kick, automatico ogni 15s).</summary>
        private async void BtnMaintenanceRekick_Click(object sender, RoutedEventArgs e)
        {
            if (!_mntEnabled) return;   // il bottone è comunque nascosto se non attiva
            SelectLog();
            BtnMaintenanceRekick.IsEnabled = false;
            Log(L.MntKicking);
            await RunAsync(() => RunMaintenanceKickPass(verbose: true));
            BtnMaintenanceRekick.IsEnabled = true;
        }

        /// <summary>Kick di tutti i player normali online (riusato da: attivazione manutenzione,
        /// pulsante "Ri-kick ora", timer automatico <see cref="MaintenanceAutoKickTick"/>).
        /// verbose=false (timer) non logga nulla se non c'è nessuno da kickare, per non spammare
        /// il log ogni 15s — logga comunque se kicka qualcuno (straggler al Character Screen).</summary>
        private bool RunMaintenanceKickPass(bool verbose)
        {
            var uids = GetOnlineNormalPlayerUIDs();
            if (uids.Count == 0)
            {
                if (verbose) Log(L.MntKickCount(0));
                return true;
            }
            Log(verbose ? L.MntKickCount(uids.Count) : L.MntAutoKicked(uids.Count));

            // L'invio del comando slash /kickuid resta SEMPRE sull'account configurato
            // (ShaiyaTaskAgent): può SOLO eseguire i wrapper allowlist, mai leggere tabelle.
            var taskRunner = new ScriptRunner(_settings);
            bool ok = true;
            foreach (var uid in uids)
            {
                string cmd = "EXEC [PSM_Cmd].[dbo].[usp_RunCommand] @service=N'ps_game', @command=N'/kickuid " + uid + "';";
                if (taskRunner.RunSqlQuery(cmd, out _, verbose ? Log : (Action<string>)null) != 0) ok = false;
            }
            return ok;
        }

        /// <summary>Timer automatico (indipendente dalla vista aperta — NON legato a PanelDashboard
        /// Visibility come le altre sonde): ogni 15s (1) controlla se la manutenzione è attiva e, se
        /// sì, ri-kicka eventuali player normali rimasti online (copre il caso di un player fermo al
        /// Character Screen: /kickuid non lo "vede" lì, ma appena rientra in game questo timer lo
        /// becca al giro successivo); (2) calcola la salute complessiva del sistema (servizi/firewall/
        /// web) e aggiorna la barra di stato persistente nell'header (visibile in OGNI vista).
        /// _headerBusy evita sovrapposizioni se un giro impiega più di 15s (rete lenta ecc.).</summary>
        private bool _headerBusy;
        private async void HeaderStatusTick()
        {
            if (_headerBusy) return;
            _headerBusy = true;
            try
            {
                MaintenanceReport rep = null;
                bool svcOk = false, fwOk = false, webOk = false;
                await RunAsync(() =>
                {
                    rep = MaintenanceReport.Load(_settings);
                    if (rep.HasTable && rep.Enabled) RunMaintenanceKickPass(verbose: false);

                    var svc = ServiceMonitor.Query();
                    int total = svc.Count(s => s.Installed);
                    if (total == 0) total = svc.Count;
                    svcOk = total > 0 && svc.Count(s => s.Running) >= total;

                    var fw = new FirewallManager().GetStatus();
                    fwOk = fw != null && fw.Profiles.Count > 0 && fw.AllEnabled;

                    if (_webServer == null) _webServer = WebServerFactory.Create(_settings);
                    var web = WebProbe.Probe(_webServer);
                    webOk = web != null && web.Reachable;
                });

                ApplyHeaderStatus(rep, svcOk, fwOk, webOk);
                // Se la Dashboard è aperta, aggiorna anche la card (contatori online) senza aspettare il TTL 20s.
                if (rep != null && PanelDashboard != null && PanelDashboard.Visibility == Visibility.Visible)
                    ApplyMaintenanceCard(rep);
            }
            finally { _headerBusy = false; }
        }

        /// <summary>Applica lo stato alla barra persistente nell'header (visibile in ogni vista).</summary>
        private void ApplyHeaderStatus(MaintenanceReport rep, bool svcOk, bool fwOk, bool webOk)
        {
            if (HeaderMntBanner == null) return;

            bool mntActive = rep != null && rep.HasTable && rep.Enabled;
            HeaderMntBanner.Visibility = mntActive ? Visibility.Visible : Visibility.Collapsed;
            if (mntActive) TbHeaderMnt.Text = L.HeaderMntBanner;

            TbHeaderPlayers.Text = (rep != null && rep.CountsOk)
                ? L.MntPlayers(rep.AdminOnline, rep.NormalOnline)
                : L.MntPlayersUnknown;

            bool allOk = svcOk && fwOk && webOk;
            HeaderSysDot.Fill = new SolidColorBrush(allOk
                ? Color.FromRgb(0x4D, 0xC8, 0x70) : Color.FromRgb(0xF4, 0x6A, 0x6A));
            TbHeaderSys.Text = allOk ? L.HeaderSysOk : L.HeaderSysWarn(svcOk, fwOk, webOk);
        }

        /// <summary>UserUID dei player normali (non-GM) attualmente online. Stessa definizione admin
        /// del gate in usp_Try_GameLogin_Taiwan (Status IN 16/32/48/64/80). Lettura diretta ADO.NET,
        /// SEMPRE Windows auth: ShaiyaTaskAgent non ha grant su PS_GameData/PS_UserData (solo sui
        /// wrapper PSM_Cmd) — vedi BtnMaintenanceToggle_Click.</summary>
        private List<int> GetOnlineNormalPlayerUIDs()
        {
            var list = new List<int>();
            try
            {
                var csb = new SqlConnectionStringBuilder
                {
                    DataSource = _settings?.SqlServer ?? "localhost",
                    InitialCatalog = "PS_GameData",
                    IntegratedSecurity = true,
                    TrustServerCertificate = true,
                    ConnectTimeout = 5,
                    ApplicationName = "ShaiyaServerHub"
                };
                using (var c = new SqlConnection(csb.ConnectionString))
                {
                    c.Open();
                    using (var cmd = new SqlCommand(
                        "SELECT DISTINCT c.UserUID FROM [PS_GameData].[dbo].[Chars] c " +
                        "JOIN [PS_UserData].[dbo].[Users_Master] u ON u.UserUID = c.UserUID " +
                        "WHERE c.LoginStatus = 1 AND c.Del = 0 AND u.[Status] NOT IN (16,32,48,64,80);", c))
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read()) list.Add(rd.GetInt32(0));
                    }
                }
            }
            catch (Exception ex) { App.WriteLog("MAINTENANCE", ex.ToString()); }
            return list;
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
                TestArea.Runner = new ScriptRunner(_settings, forceWindowsAuth: true); // Test Suite: sempre -E
                KeyRotArea.Init(_settings);   // percorso GMSecurity.ini dipende da ServerBinPath
                Log(L.SettingsSaved);
            }
        }

        // ── Funzioni: PSM_Cmd ─────────────────────────────────
        private async void BtnVerifyPsmCmd_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
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
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnInstallPsmCmd_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(L.ConfirmInstallPsm, "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                FuncArea.Log("── Installa/Ripara PSM_Cmd ──");
                FuncArea.Log("Prerequisiti: PSMagent.signed.dll in C:\\ShaiyaServer\\PSM_Client, login sysadmin, password aggiornate in 01/04.");
                await FuncArea.RunAllSqlAsync();
                FuncArea.Log("Suggerito: ora premi 'Verifica PSM_Cmd' per confermare.");
            }
            finally { if (btn != null) btn.IsEnabled = true; }
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
        // Bug reale osservato: un doppio click avviava DUE RunAllAsync() in parallelo — gli script SQL
        // che usano tabelle temp (##nome, ora #nome per essere session-scoped) collidevano tra loro
        // ("Invalid object name"/"already exists"), causando ERRORE spuri nel riepilogo. Disabilitare
        // il bottone per la durata dell'esecuzione blocca il doppio click alla radice.
        private async void BtnRunTestSuite_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                TestArea.Log("── Esecuzione test suite (SQL + PS1/BAT) ──");
                await TestArea.RunAllAsync();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        // ── SQL Server Audit (config-drift DB + login SQL falliti) ──
        private async void BtnInstallSqlAudit_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try { await TestArea.RunByNameAsync("23_sql_audit_install.sql"); }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        // ── Strumenti esterni (Sysinternals autorunsc) ────────
        private void RefreshExternalToolsStatus() => TbAutorunsStatus.Text = ExternalToolsManager.AutorunscStatusText();

        private async void BtnDownloadTools_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                TestArea.Log("── Download autorunsc.exe (Sysinternals) ──");
                await ExternalToolsManager.DownloadAutorunscAsync(TestArea.Log);
                RefreshExternalToolsStatus();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
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
            var btn = sender as Button;
            var item = ((FrameworkElement)sender).DataContext as ScriptItem;
            if (item == null) return;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                SelectLog();
                Log(L.RunningScript(item.Name));
                await RunAsync(() => _runner.RunScript(item, Log));
            }
            finally { if (btn != null) btn.IsEnabled = true; }
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
            var btn = sender as Button;
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                int rc = -1;
                await RunAsync(() => rc = TaskSchedulerService.SetEnabled(t.FullName, true, Log));
                SetTaskStatus(rc == 0 ? L.TaskEnabled(t.Name) : L.TaskActionErr(t.Name, rc), rc == 0);
                ReloadTasks();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }
        private async void BtnDisableTask_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                int rc = -1;
                await RunAsync(() => rc = TaskSchedulerService.SetEnabled(t.FullName, false, Log));
                SetTaskStatus(rc == 0 ? L.TaskDisabled(t.Name) : L.TaskActionErr(t.Name, rc), rc == 0);
                ReloadTasks();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }
        private async void BtnRunNowTask_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            if (btn != null) btn.IsEnabled = false;
            try
            {
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
            finally { if (btn != null) btn.IsEnabled = true; }
        }
        private async void BtnRemoveTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            if (MessageBox.Show(L.ConfirmRemoveTask(t.Name), "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                int rc = -1;
                await RunAsync(() => rc = TaskSchedulerService.Delete(t.FullName, Log));
                SetTaskStatus(rc == 0 ? L.TaskRemoved(t.Name) : L.TaskActionErr(t.Name, rc), rc == 0);
                ReloadTasks();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnEditTask_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            if (btn != null) btn.IsEnabled = false;
            try
            {
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
            finally { if (btn != null) btn.IsEnabled = true; }
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

        // Guardia anti-doppio-click: stesso bug reale trovato in Test Suite (due StartAll in
        // parallelo romperebbero l'ordine di dipendenza tra i servizi, come già osservato — vedi
        // ServiceMonitor.GameServiceStartOrder). Bottone disabilitato per la durata dell'operazione.
        private async void BtnStartServices_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                SelectLog();
                await RunAsync(() => ServiceMonitor.StartAll(Log));
                RefreshServices();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnStartService_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var s = ((FrameworkElement)sender).DataContext as ServiceStatus;
            if (s == null) return;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                SelectLog();
                await RunAsync(() => ServiceMonitor.Start(s.ServiceName, Log));
                RefreshServices();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnStopService_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var s = ((FrameworkElement)sender).DataContext as ServiceStatus;
            if (s == null) return;
            if (MessageBox.Show(L.ConfirmStop(s.ServiceName), "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                await RunAsync(() => ServiceMonitor.Stop(s.ServiceName, Log));
                RefreshServices();
                RefreshWebServices();   // il servizio potrebbe essere web (Apache/mysql)
            }
            finally { if (btn != null) btn.IsEnabled = true; }
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
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                SelectLog();
                await RunAsync(() =>
                {
                    ServiceMonitor.Stop(svc, Log);
                    ServiceMonitor.Start(svc, Log);
                });
                RefreshWebServices();
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnWebSslGen_Click(object sender, RoutedEventArgs e)
        {
            if (_webServer == null) return;
            string path = FindMainScript(_webServer.SslCertScript);
            if (path == null) { Log(L.WebScriptMissing(_webServer.SslCertScript)); SelectLog(); return; }
            if (MessageBox.Show(L.ConfirmWebSslGen(_webServer.SslCertScript), "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                SelectLog();
                Log(L.RunningScript(System.IO.Path.GetFileName(path)));
                await RunAsync(() => _runner.RunPowerShell(path, Log));
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnBackupWeb_Click(object sender, RoutedEventArgs e)
        {
            string path = FindMainScript("BackupFolderWEB.bat");
            if (path == null) { Log(L.WebScriptMissing("BackupFolderWEB.bat")); SelectLog(); return; }
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                SelectLog();
                Log(L.RunningScript(System.IO.Path.GetFileName(path)));
                await RunAsync(() => _runner.RunBatch(path, Log));
            }
            finally { if (btn != null) btn.IsEnabled = true; }
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
            _headerStatusTimer?.Stop();
            L.Changed -= UpdateLangButtons;
            base.OnClosed(e);
        }
    }
}
