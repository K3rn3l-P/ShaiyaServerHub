using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    public partial class App : Application
    {
        private static readonly string LogPath =
            Path.Combine(AppContext.BaseDirectory, "hub.log");

        // hub.log = SOLO programma + errori (avvio, crash, eccezioni). Niente spam di output.
        public static void WriteLog(string category, string text)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {text}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
            catch { }
        }

        // Log dedicato per l'esecuzione headless dei task pianificati (non sporca hub.log).
        private static readonly string TaskLogPath =
            Path.Combine(AppContext.BaseDirectory, "hub-tasks.log");
        public static void WriteTaskLog(string text)
        {
            try
            {
                File.AppendAllText(TaskLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { }
        }

        // Log dedicato per-task (opzione "Genera log"): stesso formato di hub-tasks.log,
        // ma su un file scelto dal task (--log "<path>"). Best effort, non solleva.
        private static void WritePerTaskLog(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            ScriptRepository.EnsureAll();

            // ── Modalità CLI crypt (rimpiazza sahcrypt.exe): usata dal ps1 di rotazione ──
            // --sah-encrypt <in> <out> <64hex>  (SAH → ESAH)
            // --sah-decrypt <in> <out> <64hex>  (ESAH → SAH)
            // Mirror di sahcrypt.exe (argc==5); exit: 0 ok · 1 uso/chiave/IO · 2 crypto/formato.
            int cidx = Array.FindIndex(e.Args, a =>
                a.Equals("--sah-encrypt", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--sah-decrypt", StringComparison.OrdinalIgnoreCase));
            if (cidx >= 0)
            {
                bool enc = e.Args[cidx].Equals("--sah-encrypt", StringComparison.OrdinalIgnoreCase);
                int rc = RunSahCrypt(enc,
                    cidx + 1 < e.Args.Length ? e.Args[cidx + 1] : null,
                    cidx + 2 < e.Args.Length ? e.Args[cidx + 2] : null,
                    cidx + 3 < e.Args.Length ? e.Args[cidx + 3] : null);
                Shutdown(rc);
                return;
            }

            // ── Modalità headless per i task pianificati: --run "<scriptPath>" ──
            int idx = Array.FindIndex(e.Args, a => a.Equals("--run", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < e.Args.Length)
            {
                // --log "<path>" opzionale → log per-task oltre a hub-tasks.log.
                int lidx = Array.FindIndex(e.Args, a => a.Equals("--log", StringComparison.OrdinalIgnoreCase));
                string taskLogFile = (lidx >= 0 && lidx + 1 < e.Args.Length) ? e.Args[lidx + 1] : null;
                // --auth win|sql opzionale → override modalità auth SQL solo per questo run.
                int auidx = Array.FindIndex(e.Args, a => a.Equals("--auth", StringComparison.OrdinalIgnoreCase));
                string authMode = (auidx >= 0 && auidx + 1 < e.Args.Length) ? e.Args[auidx + 1] : null;
                int rc = RunHeadless(e.Args[idx + 1], taskLogFile, authMode);
                Shutdown(rc);
                return;
            }

            // ── Gestione crash globali (solo modalità UI) ──
            DispatcherUnhandledException += (s, ex) =>
            {
                WriteLog("CRASH-UI", ex.Exception.ToString());
                MessageBox.Show("Errore UI (vedi hub.log):\n\n" + ex.Exception.Message,
                    "ShaiyaServerHub — Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                WriteLog("CRASH-BG", ex.ExceptionObject?.ToString() ?? "?");
            };

            WriteLog("STARTUP", "UI avviata. Log: " + LogPath);
            var win = new MainWindow();
            win.Show();
        }

        /// <summary>Esecuzione senza UI (invocata dal Task Scheduler).</summary>
        /// <param name="taskLogFile">Se valorizzato, ogni riga va anche su questo log per-task.</param>
        /// <param name="authMode">"win"/"sql" → override modalità auth SQL per questo run (null = impostazioni).</param>
        private int RunHeadless(string scriptPath, string taskLogFile = null, string authMode = null)
        {
            // Sink: hub-tasks.log sempre; log per-task in aggiunta se richiesto.
            Action<string> sink = string.IsNullOrWhiteSpace(taskLogFile)
                ? (Action<string>)WriteTaskLog
                : t => { WriteTaskLog(t); WritePerTaskLog(taskLogFile, t); };
            try
            {
                var settings = HubSettings.Load();
                // Override auth per-task (non persistito): "win" → Windows (-E), "sql" → SQL login.
                if (!string.IsNullOrWhiteSpace(authMode))
                {
                    if (authMode.Equals("win", StringComparison.OrdinalIgnoreCase) ||
                        authMode.Equals("windows", StringComparison.OrdinalIgnoreCase))
                        settings.SqlAuthMode = "Windows";
                    else if (authMode.Equals("sql", StringComparison.OrdinalIgnoreCase))
                        settings.SqlAuthMode = "Sql";
                    sink($"[auth override] {settings.SqlAuthMode}");
                }
                var runner = new ScriptRunner(settings);
                var item = new ScriptItem
                {
                    FullPath = scriptPath,
                    Kind = ScriptItem.KindFromExtension(scriptPath)
                };
                sink($"── headless: {scriptPath} ──");
                int rc = runner.RunScript(item, sink);
                sink($"exit {rc}");
                return rc;
            }
            catch (Exception ex)
            {
                // Errore = anche in hub.log (è un errore di programma).
                sink("ERRORE: " + ex.Message);
                WriteLog("RUN-ERR", ex.ToString());
                return -1;
            }
        }

        /// <summary>
        /// CLI crypt headless (rimpiazza sahcrypt.exe). Byte-compatibile col formato ESAH.
        /// Exit: 0 ok · 1 uso/chiave/IO · 2 crypto/formato (stessi codici di sahcrypt.exe).
        /// </summary>
        private static int RunSahCrypt(bool encrypt, string inPath, string outPath, string hexKey)
        {
            if (string.IsNullOrWhiteSpace(inPath) || string.IsNullOrWhiteSpace(outPath) || string.IsNullOrWhiteSpace(hexKey))
            {
                WriteLog("SAHCRYPT", "uso: --sah-encrypt|--sah-decrypt <in> <out> <64hex>");
                return 1;
            }

            byte[] key;
            try { key = SahCrypt.ParseHexKey(hexKey); }
            catch (Exception ex) { WriteLog("SAHCRYPT", "chiave non valida: " + ex.Message); return 1; }

            byte[] input;
            try { input = File.ReadAllBytes(inPath); }
            catch (Exception ex) { WriteLog("SAHCRYPT", "lettura input fallita: " + ex.Message); return 1; }

            byte[] output;
            try
            {
                output = encrypt ? SahCrypt.EncryptBuffer(input, key) : SahCrypt.DecryptBuffer(input, key);
            }
            catch (Exception ex)
            {
                // Magic mancante / tag GCM non valido (chiave sbagliata) / formato → codice 2, come sahcrypt.exe.
                WriteLog("SAHCRYPT", (encrypt ? "encrypt" : "decrypt") + " fallito: " + ex.Message);
                return 2;
            }

            try { File.WriteAllBytes(outPath, output); }
            catch (Exception ex) { WriteLog("SAHCRYPT", "scrittura output fallita: " + ex.Message); return 1; }

            return 0;
        }

    }
}
