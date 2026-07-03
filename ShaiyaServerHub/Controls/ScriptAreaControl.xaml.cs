using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub.Controls
{
    /// <summary>
    /// Area script riutilizzabile: lista script (cartelle sql\/ps1\ proprie),
    /// toolbar (Reload / Apri cartelle / Log modale), log embedded, e uno slot
    /// "Actions" dove l'host inietta i pulsanti-funzione specifici dell'area.
    /// </summary>
    public partial class ScriptAreaControl : UserControl
    {
        private ScriptRepository _repo;
        private ScriptRunner _runner;
        private readonly StringBuilder _logBuffer = new StringBuilder();
        private string _logFile;   // log proprio dell'area (es. functions\functions.log)

        public ScriptAreaControl() { InitializeComponent(); }

        // ── Slot azioni host (XAML) ──────────────────────────
        public static readonly DependencyProperty ActionsProperty =
            DependencyProperty.Register(nameof(Actions), typeof(object), typeof(ScriptAreaControl));
        public object Actions
        {
            get => GetValue(ActionsProperty);
            set => SetValue(ActionsProperty, value);
        }

        /// <summary>Titolo mostrato nel log modale.</summary>
        public string AreaTitle { get; set; } = "Area";

        /// <summary>Runner SQL/script (aggiornato dall'host quando cambiano le impostazioni).</summary>
        public ScriptRunner Runner { get => _runner; set => _runner = value; }

        /// <summary>Collega la cartella dell'area e imposta il testo di aiuto.</summary>
        public void Init(ScriptRepository repo, string hint)
        {
            _repo = repo;
            _repo.EnsureFolders();
            // Log proprio dell'area, nella sua cartella (es. functions\functions.log).
            string folderName = new DirectoryInfo(_repo.Root).Name;
            _logFile = Path.Combine(_repo.Root, folderName + ".log");
            TbHint.Text = hint;
            Reload();
        }

        /// <summary>Percorso del file di log dell'area (per aprirlo/mostrarlo).</summary>
        public string LogFilePath => _logFile;

        public void Reload()
        {
            if (_repo == null) return;
            ScriptsList.ItemsSource = _repo.Discover();
        }

        // ── Toolbar ──────────────────────────────────────────
        private void BtnReload_Click(object sender, RoutedEventArgs e) => Reload();

        private void BtnOpenFolders_Click(object sender, RoutedEventArgs e)
        {
            if (_repo == null) return;
            _repo.EnsureFolders();
            try { Process.Start("explorer.exe", _repo.SqlDir); } catch { }
            try { Process.Start("explorer.exe", _repo.Ps1Dir); } catch { }
        }

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            var win = new LogWindow(AreaTitle, _logBuffer.ToString()) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logBuffer.Clear();
            TbAreaLog.Clear();
        }

        // ── Esecuzione script ────────────────────────────────
        private async void BtnRunAreaScript_Click(object sender, RoutedEventArgs e)
        {
            var item = ((FrameworkElement)sender).DataContext as ScriptItem;
            if (item == null) return;
            if (_runner == null) { Log("Runner non inizializzato."); return; }
            Log($"── Esecuzione: {item.Name} ──");
            await RunAsync(() => _runner.RunScript(item, Log));
        }

        /// <summary>Esegue tutti gli SQL dell'area in ordine di nome (00→NN).</summary>
        public async Task RunAllSqlAsync()
        {
            if (_repo == null || _runner == null) return;
            var sql = _repo.Discover().Where(s => s.Kind == ScriptKind.Sql)
                          .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (sql.Count == 0) { Log("Nessuno script SQL in " + _repo.SqlDir); return; }
            Log($"── Esecuzione {sql.Count} script SQL in ordine ──");
            await RunAsync(() =>
            {
                foreach (var s in sql)
                {
                    Log($"▶ {s.Name}");
                    _runner.RunScript(s, Log);
                }
            });
            Log("── Fine ──");
        }

        // ── Log (embedded + buffer per il modale) ────────────
        public void Log(string msg)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Log(msg))); return; }
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _logBuffer.AppendLine(line);
            TbAreaLog.AppendText(line + "\n");
            TbAreaLog.ScrollToEnd();
            // Log SOLO nel file proprio dell'area (NON in hub.log, che resta pulito).
            try
            {
                if (_logFile != null)
                    File.AppendAllText(_logFile,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { }
        }

        public async Task RunAsync(Action action)
        {
            try { await Task.Run(action); }
            catch (Exception ex)
            {
                App.WriteLog("EXCEPTION", ex.ToString());
                Log("ERRORE: " + ex.Message);
            }
        }
    }
}
