using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    public partial class SettingsWindow : Window
    {
        private readonly HubSettings _settings;
        private ScriptRunner _runner;
        private readonly Action<string> _log;

        public SettingsWindow(HubSettings settings, ScriptRunner runner, Action<string> log)
        {
            InitializeComponent();
            _settings = settings;
            _runner = runner;
            _log = log;
            LoadToUi();
        }

        private void LoadToUi()
        {
            TbServer.Text = _settings.SqlServer;
            TbDb.Text = _settings.DefaultDatabase;
            TbSqlUser.Text = _settings.SqlUser;
            PbSqlPass.Password = _settings.SqlPassword;
            TbBinPath.Text = _settings.ServerBinPath;

            TbXamppRoot.Text = _settings.XamppRoot;
            TbWebRoot.Text = _settings.WebRoot;
            TbWebHealth.Text = _settings.WebHealthUrl;
            bool iis = string.Equals(_settings.WebServerKind, "Iis", StringComparison.OrdinalIgnoreCase);
            RbWebIis.IsChecked = iis;
            RbWebXampp.IsChecked = !iis;

            TbWebSqlUser.Text = _settings.WebSqlUser;
            PbWebSqlPass.Password = _settings.WebSqlPassword;
            TbWebDb.Text = _settings.WebsiteDatabase;
            if (string.Equals(_settings.WebSqlAuthMode, "Windows", StringComparison.OrdinalIgnoreCase)) RbWebWin.IsChecked = true;
            else if (string.Equals(_settings.WebSqlAuthMode, "Sql", StringComparison.OrdinalIgnoreCase)) RbWebSqlLogin.IsChecked = true;
            else RbWebEnv.IsChecked = true;
            UpdateWebAuthInputs();

            bool sql = string.Equals(_settings.SqlAuthMode, "Sql", StringComparison.OrdinalIgnoreCase);
            RbSql.IsChecked = sql;
            RbWin.IsChecked = !sql;
            UpdateAuthInputs();

            // AI (Hub Brain)
            CbAiEnabled.IsChecked = _settings.AiMasterEnabled;
            PbOpenRouterKey.Password = _settings.OpenRouterApiKey;
            TbOpenRouterEndpoint.Text = _settings.OpenRouterEndpoint;
            PbTelegramToken.Password = _settings.TelegramBotToken;
            TbTelegramChatId.Text = _settings.TelegramChatId;
            TbAiSqlUser.Text = _settings.AiSqlReadUser;
            PbAiSqlPass.Password = _settings.AiSqlReadPass;
            TbAiBanSqlUser.Text = _settings.AiBanSqlUser;
            PbAiBanSqlPass.Password = _settings.AiBanSqlPass;
            TbAiAutorunsCheckInterval.Text = _settings.AiAutorunsCheckIntervalMinutes.ToString();
            TbAiAutorunsRunEvery.Text = _settings.AiAutorunsRunEveryHours.ToString();
            TbAiAutorunsStartupGrace.Text = _settings.AiAutorunsStartupGraceMinutes.ToString();
            TbSearxUrl.Text = _settings.AiWebSearchUrl;
            bool searxRest = string.Equals(_settings.AiWebSearchMode, "Rest", StringComparison.OrdinalIgnoreCase);
            RbSearxRest.IsChecked = searxRest;
            RbSearxMcp.IsChecked = !searxRest;
            UpdateSearxHint();
            RefreshCircuitBreakerStatus();

            var models = Core.Ai.ModelsConfigFile.Load();
            TbAiModelCritical.Text = models.Critical;
            TbAiModelDefault.Text = models.Default;
            TbAiModelFallback.Text = models.Fallback;
            TbAiMaxPerDay.Text = models.MaxRequestsPerDay.ToString();
            TbAiMaxPerHour.Text = models.MaxRequestsPerHour.ToString();
            CbAiModelAutoUpdate.IsChecked = models.AutoUpdateDefault;
            UpdateAiModelInfo(models);
        }

        private void UpdateAiModelInfo(Core.Ai.AiModelSlots models)
        {
            if (TbAiModelUpdateInfo == null) return;
            if (string.IsNullOrWhiteSpace(models.DefaultUpdateNote))
            {
                TbAiModelUpdateInfo.Text = L.AiModelNeverChecked;
                return;
            }
            string when = DateTime.TryParse(models.DefaultUpdatedAtUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t.ToLocalTime().ToString("dd/MM HH:mm") : "?";
            TbAiModelUpdateInfo.Text = L.AiModelLastCheck(when, models.DefaultUpdateNote);
        }

        private void ApplyFromUi()
        {
            _settings.SqlServer = TbServer.Text.Trim();
            _settings.DefaultDatabase = TbDb.Text.Trim();
            _settings.SqlUser = TbSqlUser.Text.Trim();
            _settings.SqlPassword = PbSqlPass.Password;
            _settings.ServerBinPath = TbBinPath.Text.Trim();
            _settings.SqlAuthMode = RbSql.IsChecked == true ? "Sql" : "Windows";
            _settings.XamppRoot = TbXamppRoot.Text.Trim();
            _settings.WebRoot = TbWebRoot.Text.Trim();
            _settings.WebHealthUrl = TbWebHealth.Text.Trim();
            _settings.WebServerKind = RbWebIis.IsChecked == true ? "Iis" : "Xampp";

            _settings.WebSqlUser = TbWebSqlUser.Text.Trim();
            _settings.WebSqlPassword = PbWebSqlPass.Password;
            _settings.WebsiteDatabase = TbWebDb.Text.Trim();
            _settings.WebSqlAuthMode = RbWebWin.IsChecked == true ? "Windows"
                                     : (RbWebSqlLogin.IsChecked == true ? "Sql" : "Env");

            // AI (Hub Brain)
            _settings.AiMasterEnabled = CbAiEnabled.IsChecked == true;
            _settings.OpenRouterApiKey = PbOpenRouterKey.Password;
            _settings.OpenRouterEndpoint = TbOpenRouterEndpoint.Text.Trim();
            _settings.TelegramBotToken = PbTelegramToken.Password;
            _settings.TelegramChatId = TbTelegramChatId.Text.Trim();
            _settings.AiSqlReadUser = TbAiSqlUser.Text.Trim();
            _settings.AiSqlReadPass = PbAiSqlPass.Password;
            _settings.AiBanSqlUser = TbAiBanSqlUser.Text.Trim();
            _settings.AiBanSqlPass = PbAiBanSqlPass.Password;
            _settings.AiAutorunsCheckIntervalMinutes = int.TryParse(TbAiAutorunsCheckInterval.Text.Trim(), out var acim) ? acim : 30;
            _settings.AiAutorunsRunEveryHours = int.TryParse(TbAiAutorunsRunEvery.Text.Trim(), out var areh) ? areh : 24;
            _settings.AiAutorunsStartupGraceMinutes = int.TryParse(TbAiAutorunsStartupGrace.Text.Trim(), out var asgm) ? asgm : 30;
            _settings.AiWebSearchUrl = TbSearxUrl.Text.Trim();
            _settings.AiWebSearchMode = RbSearxRest.IsChecked == true ? "Rest" : "Mcp";

            var existing = Core.Ai.ModelsConfigFile.Load(); // preserva DefaultUpdatedAtUtc/Note (non editabili qui)
            var models = new Core.Ai.AiModelSlots
            {
                Critical = TbAiModelCritical.Text.Trim(),
                Default = TbAiModelDefault.Text.Trim(),
                Fallback = TbAiModelFallback.Text.Trim(),
                MaxRequestsPerDay = int.TryParse(TbAiMaxPerDay.Text.Trim(), out var perDay) ? perDay : 1000,
                MaxRequestsPerHour = int.TryParse(TbAiMaxPerHour.Text.Trim(), out var perHour) ? perHour : 100,
                AutoUpdateDefault = CbAiModelAutoUpdate.IsChecked == true,
                DefaultUpdatedAtUtc = existing.DefaultUpdatedAtUtc,
                DefaultUpdateNote = existing.DefaultUpdateNote
            };
            Core.Ai.ModelsConfigFile.Save(models);
        }

        private void WebAuth_Changed(object sender, RoutedEventArgs e) => UpdateWebAuthInputs();
        private void UpdateWebAuthInputs()
        {
            bool sql = RbWebSqlLogin?.IsChecked == true;
            if (TbWebSqlUser != null) TbWebSqlUser.IsEnabled = sql;
            if (PbWebSqlPass != null) PbWebSqlPass.IsEnabled = sql;
        }

        private void SearxMode_Changed(object sender, RoutedEventArgs e) => UpdateSearxHint();
        private void UpdateSearxHint()
        {
            if (TbSearxHint == null) return;
            string key = RbSearxRest?.IsChecked == true ? "HintSearxRest" : "HintSearxMcp";
            TbSearxHint.Text = TryFindResource(key) as string ?? "";
        }

        private void AuthMode_Changed(object sender, RoutedEventArgs e) => UpdateAuthInputs();
        private void UpdateAuthInputs()
        {
            bool sql = RbSql.IsChecked == true;
            if (TbSqlUser != null) TbSqlUser.IsEnabled = sql;
            if (PbSqlPass != null) PbSqlPass.IsEnabled = sql;
        }

        // ── Actions ───────────────────────────────────────────
        private async void BtnTestSql_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                ApplyFromUi();
                _runner = new ScriptRunner(_settings);
                SetSqlStatus(L.SqlTesting, 0xE8, 0xC8, 0x81);
                int rc = -1;
                await Task.Run(() =>
                {
                    string outp;
                    rc = _runner.RunSqlQuery("SELECT SUSER_SNAME() AS login, @@VERSION AS ver;", out outp, _log);
                    _log?.Invoke(rc == 0 ? "✓ Test SQL OK." : $"✗ Test SQL exit {rc}");
                });
                if (rc == 0) SetSqlStatus(L.SqlTestOk, 0x4D, 0xC8, 0x70);
                else SetSqlStatus(L.SqlTestFail(rc), 0xF4, 0x6A, 0x6A);
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void BtnRegisterCaller_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                ApplyFromUi();
                _runner = new ScriptRunner(_settings);
                SetSqlStatus(L.SqlTesting, 0xE8, 0xC8, 0x81);
                int rc = -1;
                await Task.Run(() =>
                {
                    rc = CommandChannel.RegisterCurrentLoginAsEconomy(_runner, _log);
                    _log?.Invoke(rc == 0 ? L.RegisterCallerDone : $"✗ exit {rc}");
                });
                if (rc == 0) SetSqlStatus(L.RegisterOk, 0x4D, 0xC8, 0x70);
                else SetSqlStatus(L.RegisterFail(rc), 0xF4, 0x6A, 0x6A);
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private void SetSqlStatus(string text, byte r, byte g, byte b)
        {
            TbSqlStatus.Text = text;
            TbSqlStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(r, g, b));
        }

        private async void BtnAiModelRefreshNow_Click(object sender, RoutedEventArgs e)
        {
            // Serve la API key per interrogare il catalogo OpenRouter: usa quella già in Impostazioni
            // (anche se non ancora salvata in questa sessione, come per il resto della finestra).
            var tempSettings = new HubSettings { OpenRouterApiKey = PbOpenRouterKey.Password };
            BtnAiModelRefreshNow.IsEnabled = false;
            try
            {
                TbAiModelUpdateInfo.Text = L.AiModelChecking;

                var router = new Core.Ai.ModelRouter();
                bool ok = false; string note = null;
                await Task.Run(() => ok = router.TryAutoUpdateDefault(tempSettings, force: true, out note));

                var models = Core.Ai.ModelsConfigFile.Load(); // TryAutoUpdateDefault salva già se ok
                if (ok) TbAiModelDefault.Text = models.Default;
                CbAiModelAutoUpdate.IsChecked = models.AutoUpdateDefault;
                UpdateAiModelInfo(models);
                if (!ok) TbAiModelUpdateInfo.Text = note; // errore: mostra motivo invece del "mai verificato"
            }
            finally { BtnAiModelRefreshNow.IsEnabled = true; }
        }

        private void RefreshCircuitBreakerStatus()
        {
            if (TbCircuitBreakerStatus == null) return;
            if (Core.Ai.PolicyGate.IsCircuitBreakerTripped)
            {
                var at = Core.Ai.PolicyGate.CircuitBreakerTrippedAtUtc;
                TbCircuitBreakerStatus.Text = L.CircuitBreakerTripped(at?.ToLocalTime());
                TbCircuitBreakerStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF4, 0x6A, 0x6A));
                BtnResetCircuitBreaker.IsEnabled = true;
            }
            else
            {
                TbCircuitBreakerStatus.Text = L.CircuitBreakerOk;
                TbCircuitBreakerStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4D, 0xC8, 0x70));
                BtnResetCircuitBreaker.IsEnabled = false;
            }
        }

        private void BtnResetCircuitBreaker_Click(object sender, RoutedEventArgs e)
        {
            Core.Ai.PolicyGate.ResetCircuitBreaker();
            RefreshCircuitBreakerStatus();
        }

        private async void BtnDetectChatId_Click(object sender, RoutedEventArgs e)
        {
            string token = PbTelegramToken.Password;
            TbTelegramStatus.Text = L.TgDetecting;
            TbTelegramStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE8, 0xC8, 0x81));
            BtnDetectChatId.IsEnabled = false;
            try
            {
                string chatId = null, error = null;
                bool found = false;
                await Task.Run(() => found = Core.Ai.TelegramHelper.DetectChatId(token, out chatId, out error));

                if (found)
                {
                    TbTelegramChatId.Text = chatId;
                    TbTelegramStatus.Text = L.TgDetectOk(chatId);
                    TbTelegramStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x4D, 0xC8, 0x70));
                }
                else
                {
                    TbTelegramStatus.Text = error;
                    TbTelegramStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF4, 0x6A, 0x6A));
                }
            }
            finally { BtnDetectChatId.IsEnabled = true; }
        }

        private void BtnBrowseBin_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TbBinPath.Text = dlg.SelectedPath;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ApplyFromUi();
            _settings.Save();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void BtnClose_Click(object sender, MouseButtonEventArgs e) { DialogResult = false; Close(); }
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
