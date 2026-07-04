using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub.Controls
{
    /// <summary>
    /// Selettore di cadenza/trigger riutilizzabile (combo + orario/intervallo/giorni/evento).
    /// Usato come trigger principale in NewTaskControl e dentro TriggerDialog per i trigger extra.
    /// </summary>
    public partial class CadencePicker : UserControl
    {
        public CadencePicker()
        {
            InitializeComponent();
            UpdateInputs();
        }

        // ── API ──────────────────────────────────────────────
        public string SelectedCadence()
        {
            var it = CbCadence?.SelectedItem as ComboBoxItem;
            return (it?.Content as string) ?? "DAILY";
        }

        /// <summary>Precompila dal ScheduleSpec dato.</summary>
        public void SetSpec(ScheduleSpec s)
        {
            if (s == null) return;
            SelectCadence(s.Type);
            TbInterval.Text = Math.Max(1, s.Interval).ToString();
            if (!string.IsNullOrWhiteSpace(s.Time)) TbTime.Text = s.Time;
            SetDaysFromCsv(s.Days);
            TbMonthDays.Text = string.IsNullOrWhiteSpace(s.MonthDays) ? "1" : s.MonthDays;
            SetMonthsFromCsv(s.Months);
            if (!string.IsNullOrWhiteSpace(s.EventChannel)) TbEventChannel.Text = s.EventChannel;
            TbEventXPath.Text = s.EventXPath ?? "";
            TbEventDelay.Text = s.EventDelayMinutes.ToString();
            UpdateInputs();
        }

        /// <summary>Legge il trigger corrente. Ritorna false con messaggio se invalido.</summary>
        public bool TryBuild(out ScheduleSpec spec, out string error)
        {
            spec = null; error = null;
            string type = SelectedCadence();

            int interval;
            if (!int.TryParse((TbInterval.Text ?? "").Trim(), out interval) || interval < 1) interval = 1;

            string days = BuildDaysCsv();
            if (type == "WEEKLY" && string.IsNullOrEmpty(days)) { error = L.PickAtLeastOneDay; return false; }

            string monthDays = NormalizeMonthDays(TbMonthDays.Text);
            if (type == "MONTHLY" && monthDays.Length == 0) { error = L.NeedMonthDay; return false; }
            string months = BuildMonthsCsv();   // vuoto = tutti i mesi (*)

            string channel = TbEventChannel.Text.Trim();
            string xpath = TbEventXPath.Text.Trim();
            if (type == "EVENT" && string.IsNullOrWhiteSpace(xpath)) { error = L.NeedEventXPath; return false; }
            int delay;
            if (!int.TryParse((TbEventDelay.Text ?? "").Trim(), out delay) || delay < 0) delay = 0;

            spec = new ScheduleSpec
            {
                Type = type,
                Interval = interval,
                Time = TbTime.Text.Trim(),
                Days = days,
                MonthDays = monthDays,
                Months = months,
                EventChannel = string.IsNullOrWhiteSpace(channel) ? "System" : channel,
                EventXPath = xpath,
                EventDelayMinutes = delay
            };
            return true;
        }

        // ── Interno ──────────────────────────────────────────
        private void CbCadence_Changed(object sender, SelectionChangedEventArgs e) => UpdateInputs();

        private void SelectCadence(string type)
        {
            type = (type ?? "DAILY").ToUpperInvariant();
            foreach (var obj in CbCadence.Items)
                if (obj is ComboBoxItem it && (it.Content as string) == type) { CbCadence.SelectedItem = it; return; }
        }

        private void UpdateInputs()
        {
            if (TbInterval == null) return;
            string c = SelectedCadence();
            bool useInterval = c == "MINUTE" || c == "HOURLY";
            bool useTime = c == "DAILY" || c == "WEEKLY" || c == "MONTHLY" || c == "ONCE";
            bool useDays = c == "WEEKLY";
            bool useMonth = c == "MONTHLY";
            bool useEvent = c == "EVENT";
            TbInterval.IsEnabled = useInterval;
            TbTime.IsEnabled = useTime;
            PanelDays.Visibility = useDays ? Visibility.Visible : Visibility.Collapsed;
            PanelMonth.Visibility = useMonth ? Visibility.Visible : Visibility.Collapsed;
            PanelEvent.Visibility = useEvent ? Visibility.Visible : Visibility.Collapsed;
            BtnEventGen.Visibility = useEvent ? Visibility.Visible : Visibility.Collapsed;
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

        // ── MONTHLY: mesi (checkbox) e giorni-del-mese (CSV) ──
        private static readonly string[] MonthCodes =
            { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

        private CheckBox[] MonthChecks() => new[]
            { ChkJan, ChkFeb, ChkMar, ChkApr, ChkMay, ChkJun, ChkJul, ChkAug, ChkSep, ChkOct, ChkNov, ChkDec };

        private void SetMonthsFromCsv(string csv)
        {
            var set = new List<string>((csv ?? "").ToUpperInvariant()
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            var checks = MonthChecks();
            for (int i = 0; i < checks.Length; i++)
                checks[i].IsChecked = set.Contains(MonthCodes[i]);
        }

        private string BuildMonthsCsv()
        {
            var checks = MonthChecks();
            var months = new List<string>();
            for (int i = 0; i < checks.Length; i++)
                if (checks[i].IsChecked == true) months.Add(MonthCodes[i]);
            return string.Join(",", months);   // vuoto = tutti i mesi (interpretato come * a valle)
        }

        // "21", "1, 15, 31" → "1,15,21,31" (ordinati, deduplicati, solo 1..31).
        private static string NormalizeMonthDays(string csv)
        {
            var days = new SortedSet<int>();
            foreach (var tok in (csv ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok.Trim(), out int d) && d >= 1 && d <= 31) days.Add(d);
            return string.Join(",", days);
        }

        // Generatore trigger evento → compila i campi EVENT di QUESTO picker.
        private void BtnEventGen_Click(object sender, RoutedEventArgs e)
        {
            var g = new EventGenWindow { Owner = Window.GetWindow(this) };
            if (g.ShowDialog() != true || g.ResultSpec == null) return;
            TbEventChannel.Text = string.IsNullOrWhiteSpace(g.ResultSpec.EventChannel) ? "System" : g.ResultSpec.EventChannel;
            TbEventXPath.Text = g.ResultSpec.EventXPath ?? "";
            TbEventDelay.Text = g.ResultSpec.EventDelayMinutes.ToString();
            SelectCadence("EVENT");
            UpdateInputs();
        }
    }
}
