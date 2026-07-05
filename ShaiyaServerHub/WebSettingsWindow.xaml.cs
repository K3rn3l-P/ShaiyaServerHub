using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ShaiyaServerHub.Core;
using ShaiyaServerHub.Core.WebServer;

namespace ShaiyaServerHub
{
    /// <summary>Editor delle impostazioni del sito (PS_WebSite.dbo.Web_Settings), via connessione Ernoweb@.</summary>
    public partial class WebSettingsWindow : Window
    {
        private readonly HubSettings _settings;
        private readonly Action<string> _log;

        public WebSettingsWindow(HubSettings settings, Action<string> log)
        {
            InitializeComponent();
            _settings = settings;
            _log = log;
            Reload();
        }

        private async void Reload()
        {
            SetStatus("carico…", 0xE8, 0xC8, 0x81);
            System.Collections.Generic.List<WebSetting> list = null;
            await Task.Run(() => list = WebSettingsRepo.ListSettings(_settings));
            // Raggruppa per categoria (Drops / Permessi / Lucky Chest / Scheduler / Altri).
            var view = new ListCollectionView(list ?? new System.Collections.Generic.List<WebSetting>());
            view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            SettingsList.ItemsSource = view;
            SetStatus($"{list?.Count ?? 0} impostazioni", 0x7B, 0xE8, 0xC8);
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => Reload();

        private async void BtnSaveRow_Click(object sender, RoutedEventArgs e)
        {
            var st = ((FrameworkElement)sender).DataContext as WebSetting;
            if (st == null) return;
            SetStatus($"salvo {st.Key}…", 0xE8, 0xC8, 0x81);
            string err = null;
            await Task.Run(() => err = WebSettingsRepo.SaveSetting(_settings, st.Key, st.Value, _log));
            if (err == null) SetStatus($"✓ {st.Key} salvato", 0x4D, 0xC8, 0x70);
            else SetStatus($"✗ {st.Key}: {err}", 0xF4, 0x6A, 0x6A);
        }

        private void SetStatus(string text, byte r, byte g, byte b)
        {
            TbStatus.Text = text;
            TbStatus.Foreground = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void BtnClose_Click(object sender, MouseButtonEventArgs e) => Close();
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
