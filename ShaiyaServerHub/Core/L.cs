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
        public static string HintFunctions => S("Verifica/installa PSM_Cmd. Script personalizzati in functions\\psm_cmd-scc\\sql e functions\\psm_cmd-scc\\ps1.", "Verify/install PSM_Cmd. Custom scripts in functions\\psm_cmd-scc\\sql and functions\\psm_cmd-scc\\ps1.");
        public static string HintTestSuite => S("Esegui i controlli di sicurezza. Metti gli script audit in testsuite\\sql e testsuite\\ps1.", "Run security checks. Put audit scripts in testsuite\\sql and testsuite\\ps1.");
        public static string ConfirmInstallPsm => S("Installare/riparare PSM_Cmd eseguendo TUTTI gli script SQL in functions\\psm_cmd-scc\\sql in ordine?\n\nRichiede: DLL firmata in posizione, login sysadmin, password già aggiornate negli script.", "Install/repair PSM_Cmd by running ALL SQL scripts in functions\\psm_cmd-scc\\sql in order?\n\nRequires: signed DLL in place, sysadmin login, passwords already updated in scripts.");
        public static string RegisterOk => S("✓ Login abilitato nel canale comandi", "✓ Login enabled in command channel");
        public static string RegisterFail(int rc) => S($"✗ Registrazione fallita (exit {rc}, vedi Log)", $"✗ Registration failed (exit {rc}, see Log)");

        // ── Dashboard ────────────────────────────────────────
        public static string DashUpdated(DateTime t) => S($"aggiornato {t:HH:mm:ss}", $"updated {t:HH:mm:ss}");
        public static string DashSvcSub(int online, int total) =>
            online == 0 ? S($"tutti offline ({total})", $"all offline ({total})")
                        : S($"{online} attivi su {total}", $"{online} of {total} up");
        public static string DashTasksSub(int enabled, int errors) => errors > 0
            ? S($"{enabled} attivi · {errors} in errore", $"{enabled} enabled · {errors} failing")
            : S($"{enabled} attivi", $"{enabled} enabled");
        public static string DashScriptsSub(int sql, int ps) => $"{sql} SQL · {ps} PS1/BAT";
        public static string DashWebProbing => S("sonda…", "probing…");
        public static string DashWebOnline => "● ONLINE";
        public static string DashWebOffline => "○ OFFLINE";
        public static string DashFwProbing => S("lettura stato firewall…", "reading firewall state…");
        public static string DashFwOn => S("🟢  Firewall ATTIVO", "🟢  Firewall ON");
        public static string DashFwOff => S("🔴  Firewall DISATTIVO", "🔴  Firewall OFF");
        public static string DashFwPartial => S("🟠  Firewall PARZIALE", "🟠  Firewall PARTIAL");
        public static string DashFwSub(int on, int total, bool swFiltering) => S(
            $"{on}/{total} profili attivi" + (swFiltering ? " · simplewall filtra" : ""),
            $"{on}/{total} profiles on" + (swFiltering ? " · simplewall filtering" : ""));
        public static string DashFwUnknown => S("stato non rilevato", "state unknown");
        // PSM_Cmd (canale comandi) — card Dashboard
        public static string DashPsmProbing => S("verifica…", "checking…");
        public static string DashPsmOn => S("🟢  PSM_Cmd ATTIVO", "🟢  PSM_Cmd ACTIVE");
        public static string DashPsmOff => S("🔴  PSM_Cmd NON attivo", "🔴  PSM_Cmd INACTIVE");
        public static string DashPsmSub => S("canale comandi · usp_RunCommand", "command channel · usp_RunCommand");
        public static string DashPsmUnknown => S("stato non determinato", "state undetermined");
        // Test Suite — card Dashboard (esiti ultimo run dal log testsuite\testsuite.log)
        public static string DashTsNever => S("mai eseguita", "never run");
        public static string DashTsSummary(int total, DateTime when) => S(
            $"{total} controlli · {when:dd/MM HH:mm}",
            $"{total} checks · {when:MM/dd HH:mm}");

        // ── Stato servizi (lista) ────────────────────────────
        public static string SvcNotInstalled => S("— non installato", "— not installed");
        public static string SvcOnline => "● ONLINE";
        public static string SvcOffline => S("○ offline", "○ offline");

        // ── Web probe (riepilogo) ────────────────────────────
        public static string WebUnreachable => S("○ irraggiungibile", "○ unreachable");

        // ── Task: tooltip bottone Log (esito ultimo run) ─────
        public static string TaskLogTipOk => S("Ultimo run: OK — clic per vedere il log", "Last run: OK — click to view the log");
        public static string TaskLogTipErr => S("Ultimo run: ERRORE — clic per vedere il log", "Last run: ERROR — click to view the log");
        public static string TaskLogTipNone => S("Nessun log per questo task", "No log for this task");

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

        // ── PSM_Cmd: editor impostazioni / log ───────────────
        public static string PsmAuthInfo(string server) => S($"({server} · PSM_Cmd · autenticazione Windows)", $"({server} · PSM_Cmd · Windows auth)");
        public static string PsmLoaded(int a, int c) => S($"Caricati {a} comandi · {c} profili.", $"Loaded {a} commands · {c} profiles.");
        public static string PsmLoadErr => S("Errore di caricamento.", "Load error.");
        public static string PsmReadErr(string m) => S("Errore di lettura da PSM_Cmd:\n\n", "Read error from PSM_Cmd:\n\n") + m;
        public static string PsmSavedStatus(int a, int p) => S($"Salvato: {a} comandi aggiornati · {p} profili.", $"Saved: {a} commands updated · {p} profiles.");
        public static string PsmSavedBox(int a, int p) => S($"Impostazioni salvate.\n\nAllowlist: {a} righe aggiornate\nProfili chiamante: {p} righe.", $"Settings saved.\n\nAllowlist: {a} rows updated\nCaller profiles: {p} rows.");
        public static string PsmSaveErr(string m) => S("Errore in salvataggio:\n\n", "Save error:\n\n") + m;
        public static string PsmSaveErrStatus => S("Errore di salvataggio (nessuna modifica applicata).", "Save error (no change applied).");
        public static string PsmLogShown(int shown, int total) => S($"Mostrate {shown} righe (di {total} totali nel log).", $"Showing {shown} rows (of {total} total in the log).");
        public static string PsmLogReadErr(string m) => S("Errore di lettura del log:\n\n", "Log read error:\n\n") + m;
        public static string PsmPurgeWhatAll => S("TUTTE le righe del log", "ALL log rows");
        public static string PsmPurgeWhatDays(int d) => S($"le righe più vecchie di {d} giorni", $"rows older than {d} days");
        public static string PsmPurgeConfirm(string what) => S($"Cancellare {what} da GmCommandLog?\n\nOperazione irreversibile.", $"Delete {what} from GmCommandLog?\n\nIrreversible operation.");
        public static string PsmPurgeTitle => S("Pulisci log PSM_Cmd", "Purge PSM_Cmd log");
        public static string PsmPurgeDone(int n) => S($"Cancellate {n} righe dal log.", $"Deleted {n} rows from the log.");
        public static string PsmPurgeErr(string m) => S("Errore in pulizia log:\n\n", "Purge error:\n\n") + m;

        // ── Firewall ─────────────────────────────────────────
        public static string FwOn => S("ATTIVO", "ON");
        public static string FwOff => S("DISATTIVO", "OFF");
        public static string FwStateLabel(bool on) => S("Firewall: ", "Firewall: ") + (on ? FwOn : FwOff);
        public static string FwEnableBtn => S("🛡 Abilita firewall", "🛡 Enable firewall");
        public static string FwDisableBtn => S("🛑 Disabilita firewall", "🛑 Disable firewall");
        public static string FwProfilesLine(string profs) => profs;
        public static string FwConfirmEnable => S(
            "Abilitare Windows Firewall su tutti i profili (default inbound = Block)?\n\nAssicurati che le regole di gestione (SSH/RDP/SQL) siano presenti e che il tuo PC sia in LAN, per non perdere l'accesso.",
            "Enable Windows Firewall on all profiles (default inbound = Block)?\n\nMake sure the management rules (SSH/RDP/SQL) exist and your PC is on the LAN, to avoid losing access.");
        public static string FwConfirmDisable => S(
            "⚠ Disabilitare Windows Firewall su TUTTI i profili?\n\nIl server resterebbe senza filtro: tutte le porte in ascolto diventano raggiungibili. Sconsigliato in produzione.",
            "⚠ Disable Windows Firewall on ALL profiles?\n\nThe server would be left unfiltered: every listening port becomes reachable. Not recommended in production.");
        public static string FwConfirmPosture(string lan) => S(
            $"Applicare la posture consigliata (LAN {lan})?\n\nGioco/web pubblici; SSH/RDP/SQL/Ghidra solo LAN; default-block; riduzione superficie. Esegui da un PC in LAN (rischio lockout).",
            $"Apply the recommended posture (LAN {lan})?\n\nGame/web public; SSH/RDP/SQL/Ghidra LAN-only; default-block; surface reduction. Run from a LAN PC (lockout risk).");
        public static string FwConfirmRemove(string name) => S($"Rimuovere la regola '{name}'?", $"Remove rule '{name}'?");
        public static string FwTitle => S("Firewall", "Firewall");
        public static string FwDone => S("✓ Operazione completata.", "✓ Operation completed.");
        public static string FwActionErr => S("✗ Operazione fallita (vedi log).", "✗ Operation failed (see log).");
        public static string FwScriptMissing(string p) => S($"Script posture non trovato: {p}", $"Posture script not found: {p}");
        public static string FwDlgErrName => S("Nome regola non valido (lettere, numeri, spazi, - _ ).", "Invalid rule name (letters, digits, spaces, - _ ).");
        public static string FwDlgErrPort => S("Porte non valide. Usa numeri, liste (80,443) o range (5000-5100).", "Invalid ports. Use numbers, lists (80,443) or ranges (5000-5100).");
        public static string FwDlgErrCustom => S("Inserisci l'indirizzo/CIDR personalizzato (es. 203.0.113.0/24).", "Enter the custom address/CIDR (e.g. 203.0.113.0/24).");
        public static string FwDlgEditTitle => S("Modifica regola firewall", "Edit firewall rule");
        public static string FwScopePublic => S("Pubblico (Any)", "Public (Any)");
        public static string FwScopeRestricted(string remote) => S("Ristretto (", "Restricted (") + remote + ")";
        public static string FwAuditLogHeader(bool fwOn, bool sw) => S(
            $"===== Test di rete {DateTime.Now:yyyy-MM-dd HH:mm:ss} · Firewall {(fwOn ? "ON" : "OFF")}{(sw ? " · simplewall FILTRA" : "")} =====",
            $"===== Network test {DateTime.Now:yyyy-MM-dd HH:mm:ss} · Firewall {(fwOn ? "ON" : "OFF")}{(sw ? " · simplewall FILTERING" : "")} =====");
        public static string FwAuditCounts(int risk, int warn, int ok) => S(
            $"Rischi: {risk} · Attenzione: {warn} · OK: {ok}.",
            $"Risks: {risk} · Warnings: {warn} · OK: {ok}.");
        public static string FwAuditNote(bool fwOn, bool swFiltering) => S(
            (fwOn ? "Windows Firewall ATTIVO. " : "⚠ Windows Firewall DISATTIVO: tutto ciò che è in ascolto è esposto. ") +
            (swFiltering ? "simplewall STA FILTRANDO (il verdetto qui si basa sulle regole di Windows Firewall)." : ""),
            (fwOn ? "Windows Firewall ON. " : "⚠ Windows Firewall OFF: everything listening is exposed. ") +
            (swFiltering ? "simplewall IS FILTERING (the verdict here is based on Windows Firewall rules)." : ""));

        // ── Web server ───────────────────────────────────────
        public static string WebKind(string disp, string root) => S($"{disp}  ·  webroot: {root}", $"{disp}  ·  webroot: {root}");
        public static string WebProbing => S("sonda in corso…", "probing…");
        public static string WebCert(DateTime exp, int days) => S(
            $"🔒 Cert SSL scade il {exp:yyyy-MM-dd} (tra {days} giorni)",
            $"🔒 SSL cert expires {exp:yyyy-MM-dd} (in {days} days)");
        public static string WebCertNone => S("🔒 Cert SSL: non rilevato (HTTPS non risponde su 443?)", "🔒 SSL cert: not detected (HTTPS not answering on 443?)");
        public static string ConfirmRestartWeb(string svc) => S($"Riavviare il web server ({svc})? Il sito sarà offline per qualche secondo.", $"Restart the web server ({svc})? The site will be offline for a few seconds.");
        public static string ConfirmWebSslGen(string script) => S($"Rigenerare il certificato SSL eseguendo '{script}'? Riavvierà il web server.", $"Regenerate the SSL certificate by running '{script}'? It will restart the web server.");
        public static string WebScriptMissing(string script) => S($"✗ Script non trovato in scripts\\ps1: {script}", $"✗ Script not found in scripts\\ps1: {script}");
        public static string WebLogsMissing(string dir) => S($"✗ Cartella log web non trovata: {dir}", $"✗ Web log folder not found: {dir}");
    }
}
