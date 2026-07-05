using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    /// <summary>
    /// Visualizza l'audit della superficie di rete. Non esegue direttamente la sonda:
    /// riceve un provider (dal FirewallControl) che lancia l'audit, scrive il log su file
    /// e aggiorna i pallini delle regole. Così log e pallini restano coerenti col pannello.
    /// </summary>
    public partial class NetworkAuditWindow : Window
    {
        private readonly Func<Task<(List<PortAuditRow> rows, string summary)>> _provider;

        public NetworkAuditWindow(Func<Task<(List<PortAuditRow>, string)>> provider)
        {
            InitializeComponent();
            _provider = provider;
            Loaded += (s, e) => Run();
        }

        private async void Run()
        {
            TbStatus.Text = L.SqlTesting;
            try
            {
                var (rows, summary) = await _provider();
                Grid.ItemsSource = rows;
                TbSummary.Text = summary;
                TbStatus.Text = L.PsmLogShown(rows.Count, rows.Count);
            }
            catch (Exception ex)
            {
                TbStatus.Text = "ERRORE: " + ex.Message;
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => Run();
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
