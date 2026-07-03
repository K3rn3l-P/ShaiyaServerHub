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

        public static void WriteLog(string category, string text)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {text}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
            catch { }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            ScriptRepository.EnsureFolders();

            // ── Modalità headless per i task pianificati: --run "<scriptPath>" ──
            int idx = Array.FindIndex(e.Args, a => a.Equals("--run", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < e.Args.Length)
            {
                int rc = RunHeadless(e.Args[idx + 1]);
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
        private int RunHeadless(string scriptPath)
        {
            try
            {
                var settings = HubSettings.Load();
                var runner = new ScriptRunner(settings);
                var item = new ScriptItem
                {
                    FullPath = scriptPath,
                    Kind = ScriptItem.KindFromExtension(scriptPath)
                };
                WriteLog("RUN", $"headless: {scriptPath}");
                int rc = runner.RunScript(item, l => WriteLog("RUN", l));
                WriteLog("RUN", $"exit {rc}");
                return rc;
            }
            catch (Exception ex)
            {
                WriteLog("RUN-ERR", ex.ToString());
                return -1;
            }
        }
    }
}
