using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub.Controls
{
    /// <summary>
    /// Form "Nuovo task pianificato" riutilizzabile: usato sia nella vista Nuovo task
    /// sia nel modale aperto da "Pianifica". La logica di creazione (TaskSchedulerService)
    /// è condivisa → nessuna duplicazione.
    /// </summary>
    public partial class NewTaskControl : UserControl
    {
        /// <summary>Dove scrivere i log (impostato dall'host, es. MainWindow.Log).</summary>
        public Action<string> LogSink { get; set; }
        /// <summary>Sollevato dopo creazione task riuscita.</summary>
        public event Action Created;

        public NewTaskControl()
        {
            InitializeComponent();
            UpdateCadenceInputs();
        }

        /// <summary>Preseleziona lo script da pianificare + nome di default.</summary>
        public void SetScript(string fullPath)
        {
            TbSelScript.Text = fullPath;
            TbSelScript.ToolTip = fullPath;
            if (string.IsNullOrWhiteSpace(TbTaskName.Text))
                TbTaskName.Text = Path.GetFileNameWithoutExtension(fullPath);
        }

        private string SelectedCadence()
        {
            var it = CbCadence?.SelectedItem as ComboBoxItem;
            return (it?.Content as string) ?? "DAILY";
        }

        private void CbCadence_Changed(object sender, SelectionChangedEventArgs e) => UpdateCadenceInputs();

        private void UpdateCadenceInputs()
        {
            if (TbInterval == null) return;
            string c = SelectedCadence();
            bool useInterval = c == "MINUTE" || c == "HOURLY";
            bool useTime = c == "DAILY" || c == "WEEKLY" || c == "ONCE";
            bool useDays = c == "WEEKLY";
            // LOGON / BOOT / SHUTDOWN: nessun parametro di orario.
            TbInterval.IsEnabled = useInterval;
            TbTime.IsEnabled = useTime;
            PanelDays.Visibility = useDays ? Visibility.Visible : Visibility.Collapsed;
        }

        // Giorni selezionati → codici schtasks "MON,WED,FRI".
        private string BuildDaysCsv()
        {
            var days = new List<string>();
            if (ChkMon.IsChecked == true) days.Add("MON");
            if (ChkTue.IsChecked == true) days.Add("TUE");
            if (ChkWed.IsChecked == true) days.Add("WED");
            if (ChkThu.IsChecked == true) days.Add("THU");
            if (ChkFri.IsChecked == true) days.Add("FRI");
            if (ChkSat.IsChecked == true) days.Add("SAT");
            if (ChkSun.IsChecked == true) days.Add("SUN");
            return string.Join(",", days);
        }

        private void Log(string msg) => LogSink?.Invoke(msg);

        private async void BtnCreateTask_Click(object sender, RoutedEventArgs e)
        {
            string script = TbSelScript.Text.Trim();
            if (string.IsNullOrWhiteSpace(script) || !File.Exists(script))
            { Log(L.NoScriptSelected); return; }

            string name = TbTaskName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(script);

            int interval;
            if (!int.TryParse(TbInterval.Text.Trim(), out interval) || interval < 1) interval = 1;

            string type = SelectedCadence();
            string daysCsv = BuildDaysCsv();
            if (type == "WEEKLY" && string.IsNullOrEmpty(daysCsv))
            { Log(L.PickAtLeastOneDay); return; }

            var spec = new ScheduleSpec
            {
                Type = type,
                Interval = interval,
                Time = TbTime.Text.Trim(),
                Days = daysCsv
            };

            BtnCreate.IsEnabled = false;
            try
            {
                int rc = -1;
                await Task.Run(() => { rc = TaskSchedulerService.Create(name, script, spec, Log); });
                if (rc == 0)
                {
                    Log(L.TaskCreated);
                    Created?.Invoke();
                }
            }
            catch (Exception ex) { Log(L.Err(ex.Message)); }
            finally { BtnCreate.IsEnabled = true; }
        }
    }
}
