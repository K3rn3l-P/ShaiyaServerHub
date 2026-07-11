using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Scopre gli script in &lt;Root&gt;\sql e &lt;Root&gt;\ps1. Ogni "area" del programma
    /// (principale, funzioni, test suite) ha la sua istanza con le proprie cartelle,
    /// create all'avvio: l'utente ci mette gli script (vecchi e nuovi), ricaricati
    /// ad ogni apertura / Reload — senza ricompilare il programma.
    /// </summary>
    public class ScriptRepository
    {
        public string Root { get; }
        public ScriptRepository(string root) { Root = root; }

        public string SqlDir => Path.Combine(Root, "sql");
        public string Ps1Dir => Path.Combine(Root, "ps1");

        public void EnsureFolders()
        {
            Directory.CreateDirectory(SqlDir);
            Directory.CreateDirectory(Ps1Dir);
        }

        public List<ScriptItem> Discover()
        {
            EnsureFolders();
            var list = new List<ScriptItem>();

            foreach (var f in SafeEnum(SqlDir, "*.sql"))
                list.Add(new ScriptItem { FullPath = f, Kind = ScriptKind.Sql });

            foreach (var f in SafeEnum(Ps1Dir, "*.ps1")
                              .Concat(SafeEnum(Ps1Dir, "*.bat"))
                              .Concat(SafeEnum(Ps1Dir, "*.cmd")))
                list.Add(new ScriptItem { FullPath = f, Kind = ScriptItem.KindFromExtension(f) });

            return list.OrderBy(s => s.Kind).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> SafeEnum(string dir, string pattern)
        {
            try { return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories); }
            catch { return Enumerable.Empty<string>(); }
        }

        // ── Aree note ────────────────────────────────────────
        public static string AppRoot => AppContext.BaseDirectory;
        // Ogni area ha la sua cartella con dentro sql\ e ps1\ (progetto ordinato).
        public static ScriptRepository Main => new ScriptRepository(Path.Combine(AppRoot, "scripts"));
        public static ScriptRepository TestSuite => new ScriptRepository(Path.Combine(AppRoot, "testsuite"));
        // Area "Key Rotation": <AppRoot>\keyrotation\{sql,ps1}. Contiene Rotate-ShaiyaKeys.ps1
        // (editabile dall'Hub come gli altri script). Vedi KeyRotationControl.
        public static ScriptRepository KeyRotation => new ScriptRepository(Path.Combine(AppRoot, "keyrotation"));
        // Area "Manutenzione": <AppRoot>\manutenzione\{sql,ps1}. sql\ contiene notice-enter.sql,
        // notice-exit.sql, kick-normals.sql (editabili dall'Hub come gli altri script). L'orchestrazione
        // (toggle tabella + notice + kick) è in C# (MainWindow.BtnMaintenanceToggle_Click), NON in ps1:
        // serve auth SPLIT fra scrittura tabella (-E/system) e comandi slash (account configurato,
        // default ShaiyaTaskAgent) — un ps1 standalone non ha accesso a HubSettings per farlo senza
        // duplicare credenziali su disco. Le modifiche DDL (tabella + gate nel login) sono a parte,
        // eseguite manualmente dall'admin — vedi guida install SQL. Card Dashboard: vedi MaintenanceReport.
        public static ScriptRepository Maintenance => new ScriptRepository(Path.Combine(AppRoot, "manutenzione"));

        // Radice comune di tutte le funzioni (una sottocartella per scheda/tab).
        public static string FunctionsRoot => Path.Combine(AppRoot, "functions");

        /// <summary>
        /// Repository di una singola scheda "Funzioni": functions\&lt;name&gt;\ con dentro sql\ e ps1\.
        /// Ogni nuova scheda della vista Funzioni ha la sua sottocartella (es. "psm_cmd-scc").
        /// </summary>
        public static ScriptRepository Function(string name) =>
            new ScriptRepository(Path.Combine(FunctionsRoot, name));

        /// <summary>Crea tutte le cartelle script (principale + aree) all'avvio.</summary>
        public static void EnsureAll()
        {
            Main.EnsureFolders();
            Function("psm_cmd-scc").EnsureFolders();
            TestSuite.EnsureFolders();
            KeyRotation.EnsureFolders();
            Maintenance.EnsureFolders();
        }
    }
}
