using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    /// <summary>
    /// Editor delle impostazioni del canale comandi PSM_Cmd:
    ///  - Allowlist comandi (Tier / Abilitato / Descrizione)
    ///  - Profili chiamante (login SQL → Tier massimo)
    /// Legge/scrive via ADO.NET in Windows auth (sysadmin sul server).
    /// </summary>
    public partial class PsmCmdSettingsWindow : Window
    {
        private readonly PsmCmdRepository _repo;
        private readonly ObservableCollection<AllowlistRow> _allow = new ObservableCollection<AllowlistRow>();
        private readonly ObservableCollection<CallerProfileRow> _callers = new ObservableCollection<CallerProfileRow>();
        private ICollectionView _allowView;

        public PsmCmdSettingsWindow(HubSettings settings)
        {
            InitializeComponent();
            _repo = new PsmCmdRepository(settings);
            TbServerInfo.Text = L.PsmAuthInfo(settings.SqlServer);

            _allowView = CollectionViewSource.GetDefaultView(_allow);
            _allowView.Filter = AllowFilter;
            GridAllow.ItemsSource = _allowView;
            GridCallers.ItemsSource = _callers;

            LoadAll();
        }

        private void LoadAll()
        {
            try
            {
                _allow.Clear();
                foreach (var r in _repo.LoadAllowlist()) _allow.Add(r);
                _callers.Clear();
                foreach (var r in _repo.LoadCallerProfiles()) _callers.Add(r);
                TbStatus.Text = L.PsmLoaded(_allow.Count, _callers.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.PsmReadErr(ex.Message),
                    "PSM_Cmd", MessageBoxButton.OK, MessageBoxImage.Error);
                TbStatus.Text = L.PsmLoadErr;
            }
        }

        // ── Filtro allowlist ─────────────────────────────────
        private bool AllowFilter(object o)
        {
            var row = o as AllowlistRow;
            if (row == null) return false;

            // Indice 0 = "Tutti"/"All" (localizzato) → nessun filtro. Gli altri item
            // (ps_game/ps_login, SAFE/PLAYER/…) sono valori tecnici non localizzati.
            if (CbService.SelectedIndex > 0)
            {
                string svc = (CbService.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (row.Service != svc) return false;
            }
            if (CbTier.SelectedIndex > 0)
            {
                string tier = (CbTier.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (row.Tier != tier) return false;
            }

            if (ChkOnlyEnabled.IsChecked == true && !row.Enabled) return false;

            string q = TbSearch.Text?.Trim();
            if (!string.IsNullOrEmpty(q))
            {
                string hay = (row.Command + " " + row.Description).ToLowerInvariant();
                if (!hay.Contains(q.ToLowerInvariant())) return false;
            }
            return true;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => _allowView?.Refresh();
        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => _allowView?.Refresh();
        private void Filter_Changed(object sender, TextChangedEventArgs e) => _allowView?.Refresh();

        // ── Profili chiamante ────────────────────────────────
        private void BtnAddCaller_Click(object sender, RoutedEventArgs e)
        {
            var row = new CallerProfileRow { CallerLogin = "", MaxTierRank = 1 };
            _callers.Add(row);
            GridCallers.SelectedItem = row;
            GridCallers.ScrollIntoView(row);
            GridCallers.CurrentCell = new DataGridCellInfo(row, GridCallers.Columns[0]);
            GridCallers.BeginEdit();
        }

        private void BtnRemoveCaller_Click(object sender, RoutedEventArgs e)
        {
            if (GridCallers.SelectedItem is CallerProfileRow row) _callers.Remove(row);
        }

        // ── Azioni ───────────────────────────────────────────
        private void BtnReload_Click(object sender, RoutedEventArgs e) => LoadAll();

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Commit di eventuali edit di cella ancora aperti.
            GridAllow.CommitEdit(DataGridEditingUnit.Row, true);
            GridCallers.CommitEdit(DataGridEditingUnit.Row, true);

            try
            {
                int a = _repo.SaveAllowlist(_allow);
                int p = _repo.SaveCallerProfiles(_callers);
                TbStatus.Text = L.PsmSavedStatus(a, p);
                MessageBox.Show(this, L.PsmSavedBox(a, p),
                    "PSM_Cmd", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.PsmSaveErr(ex.Message),
                    "PSM_Cmd", MessageBoxButton.OK, MessageBoxImage.Error);
                TbStatus.Text = L.PsmSaveErrStatus;
            }
        }

        private void BtnGuide_Click(object sender, RoutedEventArgs e) =>
            new PsmCmdGuideWindow { Owner = this }.ShowDialog();

        // ── Chrome ───────────────────────────────────────────
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
