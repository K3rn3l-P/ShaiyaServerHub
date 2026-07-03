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

        /// <summary>Nuovo task da uno script: nome + azione di default (exe --run "script").</summary>
        public void SetScript(string fullPath)
        {
            TbSelScript.Text = fullPath;
            TbSelScript.ToolTip = fullPath;
            if (string.IsNullOrWhiteSpace(TbTaskName.Text))
                TbTaskName.Text = Path.GetFileNameWithoutExtension(fullPath);
            // Azione di default: il Hub esegue lo script in headless.
            TbProgram.Text = TaskSchedulerService.ExePath;
            TbArguments.Text = $"--run \"{fullPath}\"";
            TbStartIn.Text = TaskSchedulerService.ExeDir;
        }

        /// <summary>Modale Edit: precompila tutto dai valori del task esistente.</summary>
        public void LoadForEdit(TaskDefinition def)
        {
            TbTaskName.Text = def.Name;
            TbTaskName.IsReadOnly = true;              // il nome identifica il task → non modificabile in edit
            TbSelScript.Text = "(modifica task esistente)";
            TbProgram.Text = def.Program;
            TbArguments.Text = def.Arguments;
            TbStartIn.Text = def.WorkingDir;

            SelectCadence(def.Schedule.Type);
            TbInterval.Text = Math.Max(1, def.Schedule.Interval).ToString();
            if (!string.IsNullOrWhiteSpace(def.Schedule.Time)) TbTime.Text = def.Schedule.Time;
            SetDaysFromCsv(def.Schedule.Days);
            UpdateCadenceInputs();
        }

        private void SelectCadence(string type)
        {
            type = (type ?? "DAILY").ToUpperInvariant();
            foreach (var obj in CbCadence.Items)
                if (obj is ComboBoxItem it && (it.Content as string) == type)
                { CbCadence.SelectedItem = it; return; }
        }

        private void SetDaysFromCsv(string csv)
        {
            var set = new List<string>((csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            ChkMon.IsChecked = set.Contains("MON");
            ChkTue.IsChecked = set.Contains("TUE");
            ChkWed.IsChecked = set.Contains("WED");
            ChkThu.IsChecked = set.Contains("THU");
            ChkFri.IsChecked = set.Contains("FRI");
            ChkSat.IsChecked = set.Contains("SAT");
            ChkSun.IsChecked = set.Contains("SUN");
        }

        private void BtnBrowseProgram_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Eseguibili/script|*.exe;*.bat;*.cmd;*.ps1|Tutti|*.*" };
            if (dlg.ShowDialog() == true) TbProgram.Text = dlg.FileName;
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
            string name = TbTaskName.Text.Trim();
            string program = TbProgram.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { Log(L.NeedTaskName); return; }
            if (string.IsNullOrWhiteSpace(program)) { Log(L.NeedProgram); return; }

            int interval;
            if (!int.TryParse(TbInterval.Text.Trim(), out interval) || interval < 1) interval = 1;

            string type = SelectedCadence();
            string daysCsv = BuildDaysCsv();
            if (type == "WEEKLY" && string.IsNullOrEmpty(daysCsv))
            { Log(L.PickAtLeastOneDay); return; }

            var spec = new ScheduleSpec { Type = type, Interval = interval, Time = TbTime.Text.Trim(), Days = daysCsv };
            string arguments = TbArguments.Text;         // può essere vuoto
            string workingDir = TbStartIn.Text.Trim();

            BtnCreate.IsEnabled = false;
            try
            {
                int rc = -1;
                await Task.Run(() => { rc = TaskSchedulerService.Create(name, spec, program, arguments, workingDir, Log); });
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
