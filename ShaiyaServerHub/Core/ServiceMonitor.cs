using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;

namespace ShaiyaServerHub.Core
{
    public class ServiceStatus
    {
        public string ServiceName { get; set; }    // nome servizio Windows
        public string DisplayName { get; set; }
        public bool Installed { get; set; }
        public bool Running { get; set; }

        public string StatusText =>
            !Installed ? "— non installato" : (Running ? "● ONLINE" : "○ offline");

        private static readonly System.Windows.Media.Brush Green = Freeze(0x4D, 0xC8, 0x70);
        private static readonly System.Windows.Media.Brush Red = Freeze(0xF4, 0x6A, 0x6A);
        private static readonly System.Windows.Media.Brush Gray = Freeze(0x66, 0x70, 0x7A);

        public System.Windows.Media.Brush StatusBrush => !Installed ? Gray : (Running ? Green : Red);

        // Stop solo se online+installato; Start solo se offline+installato.
        public System.Windows.Visibility StopVis =>
            (Installed && Running) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility StartVis =>
            (Installed && !Running) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        private static System.Windows.Media.Brush Freeze(byte r, byte g, byte b)
        {
            var br = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }
    }

    /// <summary>
    /// Stato/controllo dei SERVIZI Windows del server Shaiya (via ServiceController).
    /// Richiede privilegi amministrativi per Start/Stop (l'app gira elevata).
    /// </summary>
    public static class ServiceMonitor
    {
        // (serviceName, displayName) — nomi servizi reali (da AutoAvvioServiziShaiya.ps1).
        private static readonly (string svc, string disp)[] Watched =
        {
            ("MSSQLSERVER",     "SQL Server"),
            ("PSM_AgentServer", "PSM Agent (server)"),
            ("PSM_AgentClient", "PSM Agent (client)"),
            ("ps_session",      "Session"),
            ("ps_userLog",      "User Log"),
            ("ps_login",        "Login server"),
            ("ps_gameLog",      "Game Log"),
            ("ps_dbAgent",      "DB Agent"),
            ("ps_game",         "Game server"),
        };

        // Ordine di avvio "Avvia servizi" (SQL + agenti prima, poi i servizi di gioco).
        private static readonly string[] StartOrder =
        {
            "MSSQLSERVER", "PSM_AgentServer", "PSM_AgentClient",
            "ps_session", "ps_userLog", "ps_login", "ps_gameLog", "ps_dbAgent", "ps_game"
        };

        public static List<ServiceStatus> Query() => Query(Watched);

        /// <summary>
        /// Stato di un insieme arbitrario di servizi (svc, displayName). Usato dalla vista
        /// gioco (Watched) e dalla vista Web (servizi del web server scelto), tenute separate.
        /// </summary>
        public static List<ServiceStatus> Query((string svc, string disp)[] which)
        {
            ServiceController[] all;
            try { all = ServiceController.GetServices(); }
            catch { all = Array.Empty<ServiceController>(); }

            var map = new Dictionary<string, ServiceControllerStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var sc in all)
            {
                try { map[sc.ServiceName] = sc.Status; } catch { }
                finally { sc.Dispose(); }
            }

            var list = new List<ServiceStatus>();
            foreach (var (svc, disp) in which)
            {
                bool installed = map.TryGetValue(svc, out var st);
                list.Add(new ServiceStatus
                {
                    ServiceName = svc,
                    DisplayName = disp,
                    Installed = installed,
                    Running = installed && st == ServiceControllerStatus.Running
                });
            }
            return list;
        }

        public static void Start(string serviceName, Action<string> log)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    { log?.Invoke($"{serviceName}: già in esecuzione."); return; }
                    log?.Invoke($"Start-Service {serviceName}...");
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
                    log?.Invoke($"✓ {serviceName} avviato.");
                }
            }
            catch (InvalidOperationException)
            { log?.Invoke($"✗ {serviceName}: servizio non trovato (installato?)."); }
            catch (System.ServiceProcess.TimeoutException)
            { log?.Invoke($"⚠ {serviceName}: timeout avvio (verifica stato)."); }
            catch (Exception ex)
            { log?.Invoke($"✗ {serviceName} start errore: {ex.Message} (serve admin?)"); }
        }

        public static void Stop(string serviceName, Action<string> log)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    { log?.Invoke($"{serviceName}: già fermo."); return; }
                    if (!sc.CanStop)
                    { log?.Invoke($"✗ {serviceName}: non può essere fermato."); return; }
                    log?.Invoke($"Stop-Service {serviceName}...");
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25));
                    log?.Invoke($"✓ {serviceName} fermato.");
                }
            }
            catch (InvalidOperationException)
            { log?.Invoke($"✗ {serviceName}: servizio non trovato."); }
            catch (System.ServiceProcess.TimeoutException)
            { log?.Invoke($"⚠ {serviceName}: timeout stop (verifica stato)."); }
            catch (Exception ex)
            { log?.Invoke($"✗ {serviceName} stop errore: {ex.Message} (serve admin?)"); }
        }

        /// <summary>"Avvia servizi": avvia in ordine tutti i servizi installati non attivi.</summary>
        public static void StartAll(Action<string> log)
        {
            log?.Invoke("── Avvio servizi (in ordine) ──");
            var installed = Query().Where(s => s.Installed).Select(s => s.ServiceName)
                                   .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var svc in StartOrder)
            {
                if (!installed.Contains(svc)) { log?.Invoke($"— {svc}: non installato, salto."); continue; }
                Start(svc, log);
            }
            log?.Invoke("── Fine avvio servizi ──");
        }
    }
}
