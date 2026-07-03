using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    public partial class MainWindow : Window
    {
        private HubSettings _settings;
        private ScriptRunner _runner;
        private DispatcherTimer _svcTimer;

        public MainWindow()
        {
            InitializeComponent();
            _settings = HubSettings.Load();
            _runner = new ScriptRunner(_settings);

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
            FuncArea.AreaTitle = "Funzioni";
            FuncArea.Runner = _runner;
            FuncArea.Init(ScriptRepository.Functions, L.HintFunctions);
            TestArea.AreaTitle = "Test Suite";
            TestArea.Runner = _runner;
            TestArea.Init(ScriptRepository.TestSuite, L.HintTestSuite);

            ShowView("scripts");

            // Auto-refresh servizi ogni 3s (solo mentre la vista Servizi è aperta).
            _svcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _svcTimer.Tick += (s, e) => { if (PanelServices.Visibility == Visibility.Visible) RefreshServices(); };
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
            PanelScripts.Visibility = view == "scripts" ? Visibility.Visible : Visibility.Collapsed;
            PanelNewTask.Visibility = view == "newtask" ? Visibility.Visible : Visibility.Collapsed;
            PanelTasks.Visibility = view == "tasks" ? Visibility.Visible : Visibility.Collapsed;
            PanelServices.Visibility = view == "services" ? Visibility.Visible : Visibility.Collapsed;
            PanelFunctions.Visibility = view == "functions" ? Visibility.Visible : Visibility.Collapsed;
            PanelTestSuite.Visibility = view == "testsuite" ? Visibility.Visible : Visibility.Collapsed;
            PanelLog.Visibility = view == "log" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectLog() { if (NavLog != null) NavLog.IsChecked = true; }
        private void SelectTasks() { if (NavTasks != null) NavTasks.IsChecked = true; }

        // ── Custom chrome ─────────────────────────────────────
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
        private void BtnMin_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, MouseButtonEventArgs e) => Close();

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
                FuncArea.Runner = _runner;
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
                    : "✗ PSM_Cmd NON attivo/incompleto → usa 'Installa/Ripara' (metti gli script 00→07 in functions\\sql).");
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

        // ── Tasks ─────────────────────────────────────────────
        private void BtnReloadTasks_Click(object sender, RoutedEventArgs e) => ReloadTasks();
        private void ReloadTasks() => TasksList.ItemsSource = TaskSchedulerService.List();

        private async void BtnEnableTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            await RunAsync(() => TaskSchedulerService.SetEnabled(t.FullName, true, Log));
            ReloadTasks();
        }
        private async void BtnDisableTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            await RunAsync(() => TaskSchedulerService.SetEnabled(t.FullName, false, Log));
            ReloadTasks();
        }
        private async void BtnRemoveTask_Click(object sender, RoutedEventArgs e)
        {
            var t = ((FrameworkElement)sender).DataContext as ScheduledTaskInfo;
            if (t == null) return;
            if (MessageBox.Show(L.ConfirmRemoveTask(t.Name), "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunAsync(() => TaskSchedulerService.Delete(t.FullName, Log));
            ReloadTasks();
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
