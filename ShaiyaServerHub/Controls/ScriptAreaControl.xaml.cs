using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        private List<ScriptItem> _items;   // stessa lista bindata (per aggiornare i badge)
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

        /// <summary>Mostra/nasconde il pulsante "Nuovo file" nella toolbar (default: mostrato).</summary>
        public bool ShowNewFile
        {
            get => BtnNewFile.Visibility == Visibility.Visible;
            set => BtnNewFile.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

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
            _items = _repo.Discover();
            HydrateOutcomes();   // ripristina badge/log dell'ultimo run dal file (persistono a reload/riavvio)
            ScriptsList.ItemsSource = _items;
        }

        /// <summary>
        /// Popola RunStatus + LastLog di ogni script dal log dell'area (ultimo run per-script).
        /// Senza questo, i badge PASS/WARN/FAIL restano vuoti finché non si riesegue lo script
        /// nella sessione corrente.
        /// </summary>
        private void HydrateOutcomes()
        {
            try
            {
                if (_logFile == null || _items == null) return;
                var map = AreaLogParser.ParsePerScript(_logFile);
                if (map.Count == 0) return;
                foreach (var it in _items)
                    if (map.TryGetValue(it.Name, out var o))
                    {
                        it.LastLog = o.Log;
                        it.RunStatus = o.Status;   // il setter notifica → aggiorna il badge
                    }
            }
            catch { }
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

        // Badge esito cliccabile → apre il log del singolo script.
        private void BtnScriptStatus_Click(object sender, RoutedEventArgs e)
        {
            var item = ((FrameworkElement)sender).DataContext as ScriptItem;
            if (item == null) return;
            string title = item.Name + (string.IsNullOrEmpty(item.RunStatus) ? "" : "  —  " + item.RunStatus);
            new LogWindow(title, item.LastLog ?? "") { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logBuffer.Clear();
            TbAreaLog.Clear();
        }

        // ── Editor file (modifica veloce / nuovo file nell'area) ──
        private void BtnEditAreaScript_Click(object sender, RoutedEventArgs e)
        {
            var item = ((FrameworkElement)sender).DataContext as ScriptItem;
            if (item == null) return;
            var win = new EditorWindow(item.FullPath) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true) Reload();
        }

        private void BtnNewAreaFile_Click(object sender, RoutedEventArgs e)
        {
            if (_repo == null) return;
            _repo.EnsureFolders();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = L.NewFilePrompt,
                InitialDirectory = _repo.SqlDir,
                Filter = "SQL (*.sql)|*.sql|PowerShell (*.ps1)|*.ps1|Batch (*.bat)|*.bat|Tutti|*.*",
                FilterIndex = 1,
                OverwritePrompt = false
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            string path = dlg.FileName;
            try { if (!File.Exists(path)) File.WriteAllText(path, "", Encoding.UTF8); }
            catch (Exception ex) { Log(L.Err(ex.Message)); return; }
            var win = new EditorWindow(path) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true) Reload();
        }

        // ── Esecuzione script ────────────────────────────────
        private async void BtnRunAreaScript_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = ((FrameworkElement)sender).DataContext as ScriptItem;
            if (item == null) return;
            if (_runner == null) { Log("Runner non inizializzato."); return; }
            if (btn != null) btn.IsEnabled = false;
            try
            {
                Log($"── Esecuzione: {item.Name} ──");
                string status = null;
                await RunAsync(() => status = RunOne(item));
                LogVerdict(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(item.Name, status) });
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        /// <summary>
        /// Riepilogo TOTALE/VERDETTO — stesso formato usato da <see cref="RunListAsync"/> per "Esegui
        /// tutto", ora anche per il run di un singolo script: senza, l'esito di uno script isolato (es.
        /// 22_autoruns_baseline.ps1) si perdeva nel log grezzo, difficile da leggere a colpo d'occhio.
        /// </summary>
        private void LogVerdict(List<KeyValuePair<string, string>> results)
        {
            int p = 0, w = 0, f = 0, e = 0, i = 0;
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
            }
            Log($"TOTALE: PASS={p}  WARN={w}  FAIL={f}  ERRORE={e}  INFO={i}  (su {results.Count})");
            string verdict = (f > 0 || e > 0) ? "❌ AZIONE RICHIESTA (FAIL/errori)"
                           : (w > 0) ? "⚠ REVISIONARE I WARNING"
                           : "✓ TUTTO OK";
            Log("VERDETTO: " + verdict);
        }

        /// <summary>
        /// Esegue uno script, cattura il SUO log e imposta l'esito sul badge cliccabile.
        /// Ritorna lo status (PASS/WARN/FAIL/ERRORE/INFO).
        /// </summary>
        private string RunOne(ScriptItem item)
        {
            var sb = new StringBuilder();
            string status = null;
            int rc = _runner.RunScript(item, line =>
            {
                Log(line);              // log dell'area (completo)
                sb.AppendLine(line);    // log del singolo script

                // Esito da: SQL "RESULT: PASS" oppure check PowerShell "Status : PASS"
                // (i check ps ritornano un oggetto con proprietà Status).
                string rest = null;
                int idx = line.IndexOf("RESULT:", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    rest = line.Substring(idx + 7);
                }
                else
                {
                    string t = line.TrimStart();
                    if (t.StartsWith("Status", StringComparison.OrdinalIgnoreCase))
                    {
                        int c = t.IndexOf(':');
                        if (c >= 0) rest = t.Substring(c + 1);
                    }
                }
                if (rest != null)
                {
                    rest = rest.TrimStart();
                    if (rest.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) status = "FAIL";
                    else if (rest.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)) status = "WARN";
                    else if (rest.StartsWith("PASS", StringComparison.OrdinalIgnoreCase)) status = "PASS";
                    else if (rest.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) status = "ERRORE";
                }
            });
            if (rc != 0) status = "ERRORE";
            else if (status == null) status = "INFO";

            string log = sb.ToString();
            Dispatcher.Invoke(() => { item.LastLog = log; item.RunStatus = status; });
            return status;
        }

        /// <summary>Esegue UN singolo script per nome file esatto (es. "23_sql_audit_install.sql") —
        /// usato da pulsanti dedicati tipo "Installa/Verifica" che devono lanciare uno script specifico
        /// senza passare dalla riga della lista. Stesso riepilogo TOTALE/VERDETTO del run per-riga.</summary>
        public async Task RunByNameAsync(string fileName)
        {
            if (_repo == null || _runner == null) return;
            if (_items == null) Reload();
            var item = _items?.FirstOrDefault(s => string.Equals(s.Name, fileName, StringComparison.OrdinalIgnoreCase));
            if (item == null) { Log($"Script '{fileName}' non trovato in {_repo.Root}."); return; }
            Log($"── Esecuzione: {item.Name} ──");
            string status = null;
            await RunAsync(() => status = RunOne(item));
            LogVerdict(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(item.Name, status) });
        }

        /// <summary>Esegue SOLO gli SQL in ordine (usato dall'install PSM_Cmd).</summary>
        public Task RunAllSqlAsync()
        {
            if (_repo == null || _runner == null) return Task.CompletedTask;
            if (_items == null) Reload();
            var list = _items.Where(s => s.Kind == ScriptKind.Sql)
                             .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return RunListAsync(list);
        }

        /// <summary>Esegue TUTTO: prima gli SQL, poi PS1/BAT (ognuno in ordine di nome).</summary>
        public Task RunAllAsync()
        {
            if (_repo == null || _runner == null) return Task.CompletedTask;
            if (_items == null) Reload();
            var sql = _items.Where(s => s.Kind == ScriptKind.Sql)
                            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);
            var other = _items.Where(s => s.Kind != ScriptKind.Sql)
                              .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);
            return RunListAsync(sql.Concat(other).ToList());
        }

        /// <summary>Esegue una lista di script in ordine, con badge per-script + riassunto finale.</summary>
        private async Task RunListAsync(List<ScriptItem> items)
        {
            if (items == null || items.Count == 0) { Log("Nessuno script da eseguire in " + _repo.Root); return; }
            Log($"── Esecuzione {items.Count} script in ordine ──");

            var results = new List<KeyValuePair<string, string>>();
            await RunAsync(() =>
            {
                foreach (var s in items)
                {
                    Log($"▶ {s.Name}");
                    results.Add(new KeyValuePair<string, string>(s.Name, RunOne(s)));
                }
            });

            // Attende che TUTTI gli append a priorità Normal siano processati:
            // il riassunto compare sempre in fondo (sicurezza extra oltre l'attesa EOF).
            await Dispatcher.Yield(DispatcherPriority.Background);

            // ── Riassunto totale ── solo le righe "macchiate" (non-PASS/INFO) elencate per nome: con
            // 20+ script il PASS di ognuno è rumore, quello che serve vedere subito è cosa NON va.
            Log("═════════════ RIASSUNTO ═════════════");
            foreach (var r in results)
                if (r.Value == "WARN" || r.Value == "FAIL" || r.Value == "ERRORE")
                    Log($"  [{r.Value,-6}] {r.Key}");
            LogVerdict(results);
            Log("═════════════════════════════════════");
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
