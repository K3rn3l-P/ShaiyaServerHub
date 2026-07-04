using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    /// <summary>
    /// Generatore trigger "su evento" semplice: cerca e scegli un SERVIZIO dalla lista
    /// (niente da digitare/ricordare — EventID 7036 è implicito), scegli QUANDO scattare
    /// (qualsiasi cambio / avviato / arrestato) e "Usa nel task". Il filtro XPath si aggiorna
    /// live. Restituisce una ScheduleSpec EVENT pronta.
    /// </summary>
    public partial class EventGenWindow : Window
    {
        public ScheduleSpec ResultSpec { get; private set; }

        private List<ServiceEntry> _all = new List<ServiceEntry>();
        private string _runningText, _stoppedText;   // param2 localizzati (dagli eventi del server)
        private bool _statesScanned;

        public EventGenWindow()
        {
            InitializeComponent();

            CbState.Items.Add(L.StateAnyChange);   // index 0 → qualsiasi cambio (nessun param2)
            CbState.Items.Add(L.StateStartedOpt);  // index 1 → avviato
            CbState.Items.Add(L.StateStoppedOpt);  // index 2 → arrestato
            CbState.SelectedIndex = 0;

            Loaded += async (s, e) => { await LoadServicesAsync(); ScanStatesInBackground(); Regenerate(); };
        }

        // ── Carica servizi ───────────────────────────────────
        private async Task LoadServicesAsync()
        {
            BtnReload.IsEnabled = false;
            TbStatus.Text = L.EvGenLoading;
            try
            {
                _all = await Task.Run(() => EventTriggerBuilder.ListServices());
                ApplyFilter();
                TbStatus.Text = L.EvGenLoaded(_all.Count);
            }
            catch (Exception ex) { TbStatus.Text = L.Err(ex.Message); }
            finally { BtnReload.IsEnabled = true; }
        }

        private async void ScanStatesInBackground()
        {
            if (_statesScanned) return;
            try
            {
                var r = await Task.Run(() =>
                {
                    string run, stop;
                    EventTriggerBuilder.ScanStateTexts(out run, out stop);
                    return Tuple.Create(run, stop);
                });
                _runningText = r.Item1; _stoppedText = r.Item2;
            }
            catch { }
            _statesScanned = true;
            UpdateStateNote();
            Regenerate();
        }

        // ── Filtro lista ─────────────────────────────────────
        private void TbSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            string q = (TbSearch.Text ?? "").Trim();
            IEnumerable<ServiceEntry> view = _all;
            if (q.Length > 0)
                view = _all.Where(s =>
                    (s.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (s.DisplayName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            var sel = LbServices.SelectedItem as ServiceEntry;
            LbServices.ItemsSource = view.ToList();
            if (sel != null) LbServices.SelectedItem = LbServices.Items.Cast<ServiceEntry>()
                                 .FirstOrDefault(s => s.Name == sel.Name);
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => _ = LoadServicesAsync();

        private void LbServices_SelectionChanged(object sender, SelectionChangedEventArgs e) => Regenerate();
        private void CbState_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateStateNote(); Regenerate(); }

        // ── Stato → param2 (localizzato) ─────────────────────
        private string SelectedStateText()
        {
            switch (CbState.SelectedIndex)
            {
                case 1: return _runningText;   // avviato (null se non rilevato → qualsiasi)
                case 2: return _stoppedText;   // arrestato
                default: return null;          // qualsiasi cambio
            }
        }

        private void UpdateStateNote()
        {
            if (CbState.SelectedIndex == 0) { TbStateNote.Text = ""; return; }
            bool known = SelectedStateText() != null;
            TbStateNote.Text = known ? "" : (_statesScanned ? L.EvGenStateUnknown : L.EvGenStateScanning);
        }

        // ── Genera filtro ────────────────────────────────────
        private void Regenerate()
        {
            if (TbGenerated == null) return;
            var svc = LbServices.SelectedItem as ServiceEntry;
            string display = svc?.DisplayName;
            string xpath = EventTriggerBuilder.BuildXPath(7036, EventTriggerBuilder.ScmProvider, display, SelectedStateText());
            TbGenerated.Text = EventTriggerBuilder.BuildQueryListXml("System", xpath);
            TbGenerated.Tag = xpath;
        }

        // ── Usa nel task ─────────────────────────────────────
        private void BtnUse_Click(object sender, RoutedEventArgs e)
        {
            var svc = LbServices.SelectedItem as ServiceEntry;
            if (svc == null) { TbStatus.Text = L.EvGenPickService; return; }
            string xpath = TbGenerated.Tag as string;
            if (string.IsNullOrWhiteSpace(xpath)) return;
            ResultSpec = new ScheduleSpec { Type = "EVENT", EventChannel = "System", EventXPath = xpath };
            DialogResult = true; Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void BtnClose_Click(object sender, MouseButtonEventArgs e) { DialogResult = false; Close(); }
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
