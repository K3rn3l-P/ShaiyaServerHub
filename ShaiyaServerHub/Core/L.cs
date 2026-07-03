using System;
using System.Windows;

namespace ShaiyaServerHub
{
    /// <summary>Localizzazione: switch dizionari Lang + stringhe di codice (log).</summary>
    public static class L
    {
        public enum Lang { It, En }
        public static Lang Current { get; private set; } = Lang.It;
        public static event Action Changed;

        public static void Switch(Lang lang)
        {
            if (Current == lang) return;
            Current = lang;

            var app = Application.Current;
            ResourceDictionary old = null;
            foreach (var d in app.Resources.MergedDictionaries)
                if (d.Source != null && d.Source.OriginalString.Contains("/Lang/")) { old = d; break; }
            if (old != null) app.Resources.MergedDictionaries.Remove(old);

            string src = lang == Lang.En ? "/Lang/en.xaml" : "/Lang/it.xaml";
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,," + src)
            });
            Changed?.Invoke();
        }

        private static string S(string it, string en) => Current == Lang.It ? it : en;

        // ── Stringhe usate dal codice ────────────────────────
        public static string Startup => S("ShaiyaServerHub avviato.", "ShaiyaServerHub started.");
        public static string ScriptsFound(int n) => S($"{n} script trovati in sql\\ e ps1\\.", $"{n} scripts found in sql\\ and ps1\\.");
        public static string NoScriptSelected => S("Seleziona prima uno script (▶ Pianifica).", "Select a script first (▶ Schedule).");
        public static string TaskCreated => S("Task creato. Ricarico lista task.", "Task created. Reloading task list.");
        public static string ConfirmRemoveTask(string n) => S($"Rimuovere il task '{n}'?", $"Remove task '{n}'?");
        public static string ConfirmStop(string n) => S($"Terminare il processo '{n}'?", $"Kill process '{n}'?");
        public static string RunningScript(string n) => S($"── Esecuzione: {n} ──", $"── Running: {n} ──");
        public static string SettingsSaved => S("Impostazioni salvate.", "Settings saved.");
        public static string Err(string m) => S("ERRORE: ", "ERROR: ") + m;
        public static string RegisterCallerDone => S("Login registrato nel canale comandi (tier ECONOMY).", "Login registered in command channel (ECONOMY tier).");
        public static string SqlTesting => S("test in corso…", "testing…");
        public static string SqlTestOk => S("✓ Connessione riuscita", "✓ Connected");
        public static string SqlTestFail(int rc) => S($"✗ Connessione fallita (exit {rc}, vedi Log)", $"✗ Connection failed (exit {rc}, see Log)");
        public static string HintFunctions => S("Verifica/installa PSM_Cmd. Script personalizzati in functions\\sql e functions\\ps1.", "Verify/install PSM_Cmd. Custom scripts in functions\\sql and functions\\ps1.");
        public static string HintTestSuite => S("Esegui i controlli di sicurezza. Metti gli script audit in testsuite\\sql e testsuite\\ps1.", "Run security checks. Put audit scripts in testsuite\\sql and testsuite\\ps1.");
        public static string ConfirmInstallPsm => S("Installare/riparare PSM_Cmd eseguendo TUTTI gli script SQL in functions\\sql in ordine?\n\nRichiede: DLL firmata in posizione, login sysadmin, password già aggiornate negli script.", "Install/repair PSM_Cmd by running ALL SQL scripts in functions\\sql in order?\n\nRequires: signed DLL in place, sysadmin login, passwords already updated in scripts.");
        public static string RegisterOk => S("✓ Login abilitato nel canale comandi", "✓ Login enabled in command channel");
        public static string RegisterFail(int rc) => S($"✗ Registrazione fallita (exit {rc}, vedi Log)", $"✗ Registration failed (exit {rc}, see Log)");
    }
}
