using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
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
        }

        private void WebAuth_Changed(object sender, RoutedEventArgs e) => UpdateWebAuthInputs();
        private void UpdateWebAuthInputs()
        {
            bool sql = RbWebSqlLogin?.IsChecked == true;
            if (TbWebSqlUser != null) TbWebSqlUser.IsEnabled = sql;
            if (PbWebSqlPass != null) PbWebSqlPass.IsEnabled = sql;
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

        private async void BtnRegisterCaller_Click(object sender, RoutedEventArgs e)
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

        private void SetSqlStatus(string text, byte r, byte g, byte b)
        {
            TbSqlStatus.Text = text;
            TbSqlStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(r, g, b));
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
