using System;
using System.Collections.Generic;
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

        /// <summary>Esegue tutti gli SQL dell'area in ordine di nome (00→NN) + riassunto finale.</summary>
        public async Task RunAllSqlAsync()
        {
            if (_repo == null || _runner == null) return;
            var sql = _repo.Discover().Where(s => s.Kind == ScriptKind.Sql)
                          .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (sql.Count == 0) { Log("Nessuno script SQL in " + _repo.SqlDir); return; }
            Log($"── Esecuzione {sql.Count} script SQL in ordine ──");

            var results = new List<KeyValuePair<string, string>>();
            await RunAsync(() =>
            {
                foreach (var s in sql)
                {
                    Log($"▶ {s.Name}");
                    string status = null;
                    int rc = _runner.RunScript(s, line =>
                    {
                        Log(line);
                        int idx = line.IndexOf("RESULT:", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            string rest = line.Substring(idx + 7).TrimStart();
                            if (rest.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) status = "FAIL";
                            else if (rest.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)) status = "WARN";
                            else if (rest.StartsWith("PASS", StringComparison.OrdinalIgnoreCase)) status = "PASS";
                        }
                    });
                    if (rc != 0) status = "ERRORE";
                    else if (status == null) status = "INFO";
                    results.Add(new KeyValuePair<string, string>(s.Name, status));
                }
            });

            // ── Riassunto totale ──
            int p = 0, w = 0, f = 0, e = 0, i = 0;
            Log("═════════════ RIASSUNTO TEST SUITE ═════════════");
            foreach (var r in results)
            {
                switch (r.Value)
                {
                    case "PASS": p++; break;
                    case "WARN": w++; break;
                    case "FAIL": f++; break;
                    case "ERRORE": e++; break;
                    default: i++; break;
                }
                Log($"  [{r.Value,-6}] {r.Key}");
            }
            Log($"TOTALE: PASS={p}  WARN={w}  FAIL={f}  ERRORE={e}  INFO={i}  (su {results.Count})");
            string verdict = (f > 0 || e > 0) ? "❌ AZIONE RICHIESTA (FAIL/errori)"
                           : (w > 0) ? "⚠ REVISIONARE I WARNING"
                           : "✓ TUTTO OK";
            Log("VERDETTO: " + verdict);
            Log("════════════════════════════════════════════════");
        }

        // ── Log (embedded + buffer per il modale) ────────────
        public void Log(string msg)
        {
            // Salta le righe vuote/di solo spazi (sqlcmd le emette tra i result set).
            if (string.IsNullOrWhiteSpace(msg)) return;
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
