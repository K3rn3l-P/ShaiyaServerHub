using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    /// <summary>Visualizzatore (sola lettura) dell'audit log PSM_Cmd.dbo.GmCommandLog, con pulizia.</summary>
    public partial class PsmCmdLogWindow : Window
    {
        private readonly PsmCmdRepository _repo;
        private bool _ready;

        public PsmCmdLogWindow(HubSettings settings)
        {
            InitializeComponent();
            _repo = new PsmCmdRepository(settings);
            TbServerInfo.Text = L.PsmAuthInfo(settings.SqlServer);
            _ready = true;
            Load();
        }

        private void Load()
        {
            try
            {
                int top = ParseTop();
                // Indice 0 = "Tutti"/"All" (localizzato) → nessun filtro. Gli altri
                // (OK/DENIED/ERROR, RunCommand/SendNotice) sono valori tecnici non localizzati.
                string result = CbResult.SelectedIndex <= 0 ? null : SelText(CbResult);
                string wrapper = CbWrapper.SelectedIndex <= 0 ? null : SelText(CbWrapper);
                string search = TbSearch.Text;

                var rows = _repo.LoadLog(top, result, wrapper, search);
                Grid.ItemsSource = rows;

                int total = _repo.LogCount();
                TbStatus.Text = L.PsmLogShown(rows.Count, total);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.PsmLogReadErr(ex.Message),
                    "PSM_Cmd", MessageBoxButton.OK, MessageBoxImage.Error);
                TbStatus.Text = L.PsmLoadErr;
            }
        }

        private int ParseTop()
        {
            string s = SelText(CbTop);
            return int.TryParse(s, out int n) ? n : 200;
        }

        private static string SelText(ComboBox cb) =>
            (cb.SelectedItem as ComboBoxItem)?.Content?.ToString();

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) Load(); }
        private void BtnApply_Click(object sender, RoutedEventArgs e) => Load();

        // ── Pulizia log ──────────────────────────────────────
        private void BtnPurge_Click(object sender, RoutedEventArgs e)
        {
            // Indice combo: 0=90gg, 1=30gg, 2=7gg, 3=TUTTO (0 giorni).
            int days; string what;
            switch (CbPurge.SelectedIndex)
            {
                case 3: days = 0; what = L.PsmPurgeWhatAll; break;
                case 2: days = 7; what = L.PsmPurgeWhatDays(7); break;
                case 1: days = 30; what = L.PsmPurgeWhatDays(30); break;
                default: days = 90; what = L.PsmPurgeWhatDays(90); break;
            }

            if (MessageBox.Show(this, L.PsmPurgeConfirm(what),
                    L.PsmPurgeTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            try
            {
                int deleted = _repo.ClearLog(days);
                TbStatus.Text = L.PsmPurgeDone(deleted);
                Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.PsmPurgeErr(ex.Message),
                    "PSM_Cmd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnGuide_Click(object sender, RoutedEventArgs e) =>
            new PsmCmdGuideWindow { Owner = this }.ShowDialog();

        // ── Chrome ───────────────────────────────────────────
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
