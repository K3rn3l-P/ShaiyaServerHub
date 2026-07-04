using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub.Controls
{
    /// <summary>
    /// Form "Nuovo task pianificato" riutilizzabile (vista Nuovo task + modale Pianifica/Edit).
    /// Trigger principale via CadencePicker + lista di trigger AGGIUNTIVI opzionali. La creazione
    /// (TaskSchedulerService) è condivisa: primo trigger via schtasks, gli extra appesi via PowerShell.
    /// </summary>
    public partial class NewTaskControl : UserControl
    {
        public Action<string> LogSink { get; set; }
        public event Action Created;

        // Trigger aggiuntivi (oltre al principale).
        private readonly List<ScheduleSpec> _extra = new List<ScheduleSpec>();

        public NewTaskControl()
        {
            InitializeComponent();
        }

        /// <summary>Nuovo task da uno script: nome + azione di default (exe --run "script").</summary>
        public void SetScript(string fullPath)
        {
            TbSelScript.Text = fullPath;
            TbSelScript.ToolTip = fullPath;
            if (string.IsNullOrWhiteSpace(TbTaskName.Text))
                TbTaskName.Text = Path.GetFileNameWithoutExtension(fullPath);
            TbProgram.Text = TaskSchedulerService.ExePath;
            TbArguments.Text = $"--run \"{fullPath}\"";
            TbStartIn.Text = TaskSchedulerService.ExeDir;
            SetAuth("Default");   // nuovo task → usa le impostazioni globali
        }

        /// <summary>Modale Edit: precompila tutto dai valori del task esistente (inclusi TUTTI i trigger).</summary>
        public void LoadForEdit(TaskDefinition def)
        {
            TbTaskName.Text = def.Name;
            TbTaskName.IsReadOnly = true;              // il nome identifica il task → non modificabile in edit
            TbSelScript.Text = "(modifica task esistente)";
            TbProgram.Text = def.Program;
            TbArguments.Text = def.Arguments;
            TbStartIn.Text = def.WorkingDir;
            ChkGenLog.IsChecked = def.GenLog;   // auto-rilevato dal parse (wrapper cmd / -o / --log)
            SetAuth(def.AuthMode);              // auto-rilevato dal parse (--auth)

            var triggers = def.Triggers ?? new List<ScheduleSpec>();
            PrimaryCadence.SetSpec(triggers.Count > 0 ? triggers[0] : new ScheduleSpec());
            _extra.Clear();
            _extra.AddRange(triggers.Skip(1));
            RefreshExtras();
        }

        // ── Trigger aggiuntivi ───────────────────────────────
        private void RefreshExtras()
        {
            ExtraTriggersList.ItemsSource = null;
            ExtraTriggersList.ItemsSource = _extra;
            ExtraBox.Visibility = _extra.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAddTrigger_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TriggerDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && dlg.ResultSpec != null)
            {
                _extra.Add(dlg.ResultSpec);
                RefreshExtras();
            }
        }

        private void BtnEditTrigger_Click(object sender, RoutedEventArgs e)
        {
            var spec = ((FrameworkElement)sender).DataContext as ScheduleSpec;
            if (spec == null) return;
            int i = _extra.IndexOf(spec);
            if (i < 0) return;
            var dlg = new TriggerDialog(spec, editing: true) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && dlg.ResultSpec != null)
            {
                _extra[i] = dlg.ResultSpec;
                RefreshExtras();
            }
        }

        private void BtnRemoveTrigger_Click(object sender, RoutedEventArgs e)
        {
            var spec = ((FrameworkElement)sender).DataContext as ScheduleSpec;
            if (spec == null) return;
            _extra.Remove(spec);
            RefreshExtras();
        }

        // ── Azione / guida ───────────────────────────────────
        private void BtnBrowseProgram_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Eseguibili/script|*.exe;*.bat;*.cmd;*.ps1|Tutti|*.*" };
            if (dlg.ShowDialog() == true) TbProgram.Text = dlg.FileName;
        }

        private void BtnGuide_Click(object sender, RoutedEventArgs e)
        {
            new GuideWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void Log(string msg) => LogSink?.Invoke(msg);

        // ── Auth per-task (checkbox) ─────────────────────────
        // Non spuntata = usa le impostazioni globali (come prima). Spuntata = esegui come SYSTEM (Windows -E).
        private string SelectedAuth() => ChkAuthSystem.IsChecked == true ? "Windows" : "Default";
        private void SetAuth(string mode) =>
            ChkAuthSystem.IsChecked = string.Equals(mode, "Windows", StringComparison.OrdinalIgnoreCase);

        // ── Crea / aggiorna task ─────────────────────────────
        private async void BtnCreateTask_Click(object sender, RoutedEventArgs e)
        {
            string name = TbTaskName.Text.Trim();
            string program = TbProgram.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { Log(L.NeedTaskName); return; }
            if (string.IsNullOrWhiteSpace(program)) { Log(L.NeedProgram); return; }

            // Guardia: l'exe del Hub SENZA --run partirebbe in modalità UI (WinExe headless) e resterebbe
            // appeso per sempre; se poi "Genera log" lo avvolge in "cmd > log", il redirect locka il file
            // di log in scrittura esclusiva → ogni run successivo fallisce l'append (log a 0 byte).
            if (TaskLogging.IsHubExe(program) && !TaskLogging.IsHubRun(program, TbArguments.Text))
            {
                Log(L.HubExeNeedsRun);
                return;
            }

            if (!PrimaryCadence.TryBuild(out ScheduleSpec primary, out string error)) { Log(error); return; }
            var extras = new List<ScheduleSpec>(_extra);

            string arguments = TbArguments.Text;         // può essere vuoto
            string workingDir = TbStartIn.Text.Trim();

            // ── Log su file opzionale ──
            string wrapLogFile = null;
            if (ChkGenLog.IsChecked == true)
            {
                string logFile = TaskLogging.LogPathFor(workingDir, program, arguments, name);
                if (!TaskLogging.EnsureLogDir(logFile))
                    Log(L.LogDirWarn(Path.GetDirectoryName(logFile)));
                if (TaskLogging.IsSqlcmd(program))
                    arguments = TaskLogging.AppendSqlcmdOutput(arguments, logFile);  // -o inline
                else if (TaskLogging.IsHubRun(program, arguments))
                    arguments = TaskLogging.AppendHubLog(arguments, logFile);        // --log inline (self-log, no redirect)
                else
                    wrapLogFile = logFile;                                            // wrapper cmd post-creazione
            }

            // ── Auth per-task (solo azione --run del Hub; non-Default) ──
            string authMode = SelectedAuth();
            if (!string.Equals(authMode, "Default", StringComparison.OrdinalIgnoreCase) &&
                TaskLogging.IsHubRun(program, arguments))
            {
                string aArg = TaskLogging.AuthArg(authMode);
                if (aArg.Length > 0) arguments = (arguments.TrimEnd() + " " + aArg).Trim();
            }

            BtnCreate.IsEnabled = false;
            try
            {
                string argsFinal = arguments; string wrap = wrapLogFile;
                int rc = -1;
                await Task.Run(() => { rc = TaskSchedulerService.Create(name, primary, program, argsFinal, workingDir, Log, wrap, extras); });
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
