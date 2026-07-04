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
        public static string ScriptsFound(int n) => S($"{n} script trovati in scripts\\sql e scripts\\ps1.", $"{n} scripts found in scripts\\sql and scripts\\ps1.");
        public static string NoScriptSelected => S("Seleziona prima uno script (▶ Pianifica).", "Select a script first (▶ Schedule).");
        public static string TaskCreated => S("Task creato. Ricarico lista task.", "Task created. Reloading task list.");
        public static string TaskEnabled(string n) => S($"✓ Abilitato: {n}", $"✓ Enabled: {n}");
        public static string TaskDisabled(string n) => S($"✓ Disabilitato: {n}", $"✓ Disabled: {n}");
        public static string TaskRun(string n) => S($"▶ Avviato (test): {n}", $"▶ Started (test): {n}");
        public static string TaskRemoved(string n) => S($"✓ Rimosso: {n}", $"✓ Removed: {n}");
        public static string TaskActionErr(string n, int rc) => S($"✗ Operazione fallita: {n} (exit {rc})", $"✗ Action failed: {n} (exit {rc})");
        public static string PickAtLeastOneDay => S("Seleziona almeno un giorno per WEEKLY.", "Select at least one day for WEEKLY.");
        public static string NeedMonthDay => S("Inserisci almeno un giorno del mese (1-31) per MONTHLY.", "Enter at least one day of month (1-31) for MONTHLY.");
        public static string NeedTaskName => S("Inserisci il nome del task.", "Enter the task name.");
        public static string NeedProgram => S("Inserisci il programma/script (azione).", "Enter the program/script (action).");
        public static string HubExeNeedsRun => S(
            "Azione non valida: l'eseguibile del Hub va usato SOLO con --run \"<script>\". Senza --run partirebbe la UI in background (task appeso, log bloccato). Usa 'Pianifica' su uno script.",
            "Invalid action: the Hub executable must be used ONLY with --run \"<script>\". Without --run it would launch the UI in the background (task hangs, log locked). Use 'Schedule' on a script.");
        public static string EditTaskTitle => S("Modifica task", "Edit task");
        public static string TrigDlgEditTitle => S("Modifica trigger", "Edit trigger");
        public static string TaskUpdated(string n) => S($"✓ Task aggiornato: {n}", $"✓ Task updated: {n}");
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

        // ── Editor file ──────────────────────────────────────
        public static string FileSaved => S("✓ Salvato", "✓ Saved");
        public static string ConfirmDiscardChanges => S("Ci sono modifiche non salvate. Scartarle?", "There are unsaved changes. Discard them?");
        public static string NewFilePrompt => S("Nuovo file: scegli nome e cartella (sql\\ o ps1\\).", "New file: choose name and folder (sql\\ or ps1\\).");
        public static string FileExists(string n) => S($"Esiste già un file '{n}'. Aprilo con ✎ Modifica.", $"A file '{n}' already exists. Open it with ✎ Edit.");

        // ── Guida / trigger evento ───────────────────────────
        public static string EventScanning => S("Scansione eventi locali (System, ID 7036)…", "Scanning local events (System, ID 7036)…");
        public static string EventScanNone => S("Nessun servizio trovato negli eventi 7036 (esegui l'app sul server).", "No service found in 7036 events (run the app on the server).");
        public static string EventScanFound(int n) => S($"{n} servizi trovati negli eventi 7036.", $"{n} services found in 7036 events.");
        public static string CopiedToClipboard => S("✓ Copiato negli appunti", "✓ Copied to clipboard");
        public static string NeedEventXPath => S("Inserisci il filtro XPath dell'evento (usa 🧩 Genera evento).", "Enter the event XPath filter (use 🧩 Event generator).");
        // Generatore trigger evento
        public static string EvGenLoading => S("Carico i servizi…", "Loading services…");
        public static string EvGenLoaded(int n) => S($"{n} servizi. Cerca e seleziona quello del gioco (es. ps_game).", $"{n} services. Search and pick the game one (e.g. ps_game).");
        public static string EvGenPickService => S("Seleziona prima un servizio dalla lista.", "Select a service from the list first.");
        public static string StateAnyChange => S("Qualsiasi cambio di stato (consigliato)", "Any state change (recommended)");
        public static string StateStartedOpt => S("Solo quando si AVVIA", "Only when it STARTS");
        public static string StateStoppedOpt => S("Solo quando si ARRESTA", "Only when it STOPS");
        public static string EvGenStateScanning => S("(rilevo il testo di stato dal server…)", "(detecting state text from server…)");
        public static string EvGenStateUnknown => S("stato non rilevato negli eventi → uso 'qualsiasi cambio'", "state not found in events → using 'any change'");
        public static string LogDirWarn(string dir) => S($"⚠ Cartella log non creabile: {dir}", $"⚠ Cannot create log folder: {dir}");
        public static string NoTaskLog(string n) => S($"Nessun log per '{n}' (abilita 'Genera log' in modifica, poi esegui).", $"No log for '{n}' (enable 'Write log' in edit, then run).");
    }
}
