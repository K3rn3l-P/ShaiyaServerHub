using System;
using System.Collections.Generic;
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

        // ── Strumenti esterni (Sysinternals) ──────────────────
        public static string ToolsAutorunsMissing => S("Autoruns: ⚠ non scaricato", "Autoruns: ⚠ not downloaded");
        public static string ToolsAutorunsPresent(long kb, DateTime updated) =>
            S($"Autoruns: ✓ presente ({kb} KB, {updated:yyyy-MM-dd})", $"Autoruns: ✓ present ({kb} KB, {updated:yyyy-MM-dd})");
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
        // Key Rotation — card Dashboard
        public static string DashKrNotInstalled => S("Rotazione non pianificata", "Rotation not scheduled");
        public static string DashKrError => S("🔴  Ultima rotazione in ERRORE", "🔴  Last rotation FAILED");
        public static string DashKrDisabled => S("🟠  Task disabilitato", "🟠  Task disabled");
        public static string DashKrOn => S("🟢  Rotazione pianificata attiva", "🟢  Rotation scheduled");
        public static string DashKrSub(DateTime? last, string next)
        {
            string l = last.HasValue
                ? S($"ultima rotazione {last.Value:dd/MM HH:mm}", $"last rotation {last.Value:MM/dd HH:mm}")
                : S("mai eseguita", "never run");
            string n = string.IsNullOrWhiteSpace(next) ? "" : S($" · prossimo {next}", $" · next {next}");
            return l + n;
        }

        // AI (Hub Brain) — card Dashboard
        public static string DashAiOff => S("⚪  AI disattivata", "⚪  AI disabled");
        public static string DashAiNotConfigured => S("🟠  Da configurare (API key)", "🟠  Needs setup (API key)");
        public static string DashAiOn => S("🟢  AI attiva", "🟢  AI on");
        public static string DashAiSub(int tools, bool sqlReader, bool telegram) => S(
            $"{tools} strumenti disponibili" + (sqlReader ? "" : " · login SQL AI non configurato") + (telegram ? "" : " · Telegram non configurato"),
            $"{tools} tools available" + (sqlReader ? "" : " · AI SQL login not configured") + (telegram ? "" : " · Telegram not configured"));

        // AI (Hub Brain) — vista chat
        public static string AiStatusOff => S("AI disattivata (vedi Impostazioni)", "AI disabled (see Settings)");
        public static string AiStatusNotConfigured => S("AI attiva ma non configurata (API key mancante)", "AI enabled but not configured (missing API key)");
        public static string AiStatusReady(int tools) => S($"AI pronta · {tools} strumenti", $"AI ready · {tools} tools");
        public static string AiModelInfo(string modelUsed) => S($"risposto da: {modelUsed}", $"answered by: {modelUsed}");
        public static string AiCancelled => S("⏹ Richiesta interrotta dall'utente.", "⏹ Request stopped by user.");
        public static string AiModelNeverChecked => S("mai verificato contro il catalogo OpenRouter.", "never checked against the OpenRouter catalog.");
        public static string AiModelChecking => S("verifica catalogo OpenRouter…", "checking OpenRouter catalog…");
        public static string AiModelLastCheck(string when, string note) => S($"ultima verifica {when}: {note}", $"last checked {when}: {note}");
        public static string CircuitBreakerOk => S("● Circuit breaker chiuso (nessuna anomalia rilevata).", "● Circuit breaker closed (no anomaly detected).");
        public static string CircuitBreakerTripped(System.DateTime? at) => S(
            $"🚨 Circuit breaker APERTO dalle {(at.HasValue ? at.Value.ToString("dd/MM HH:mm") : "?")} — tutte le azioni T1+ sono sospese finché non resetti.",
            $"🚨 Circuit breaker OPEN since {(at.HasValue ? at.Value.ToString("MM/dd HH:mm") : "?")} — all T1+ actions are suspended until reset.");

        // Telegram — rilevamento Chat ID
        public static string TgDetecting => S("ricerca chat…", "looking up chat…");
        public static string TgDetectOk(string chatId) => S($"✓ Chat ID trovato: {chatId}", $"✓ Chat ID found: {chatId}");

        // Manutenzione — card Dashboard (sostituisce la ex card Web, doppione con tessera KPI Web)
        public static string MntChecking => S("verifica…", "checking…");
        public static string MntNotInstalled => S("⚪  Non installata (vedi guida SQL)", "⚪  Not installed (see SQL guide)");
        public static string MntActive => S("🔴  MANUTENZIONE ATTIVA", "🔴  MAINTENANCE ACTIVE");
        public static string MntNormal => S("🟢  Normale", "🟢  Normal");
        public static string MntPlayers(int admin, int normal) => S(
            $"👤 Admin online: {admin} · Normali online: {normal}",
            $"👤 Admins online: {admin} · Normal online: {normal}");
        public static string MntPlayersUnknown => S("Conteggio online non disponibile", "Online count unavailable");
        public static string MntSub(bool hasTable, DateTime? changedAt, string changedBy)
        {
            if (!hasTable)
                return S("Esegui i file 8. e 9. nella guida install SQL", "Run files 8. and 9. in the SQL install guide");
            if (!changedAt.HasValue)
                return S("mai modificata", "never changed");
            string who = string.IsNullOrWhiteSpace(changedBy) ? "" : $" ({changedBy})";
            return S($"ultima modifica {changedAt.Value:dd/MM HH:mm}{who}", $"last change {changedAt.Value:MM/dd HH:mm}{who}");
        }
        public static string MntBtnEnter => S("Attiva manutenzione", "Enter maintenance");
        public static string MntBtnExit => S("Termina manutenzione", "Exit maintenance");
        public static string MntConfirmTitle => S("Modalità Manutenzione", "Maintenance Mode");
        public static string MntConfirmEnter => S(
            "Attivare la modalità manutenzione?\n\nI player normali online verranno kickati SUBITO e il login " +
            "verrà negato ai non-admin fino a disattivazione. Gli admin whitelisted continuano a entrare.",
            "Enable maintenance mode?\n\nNormal players currently online will be kicked IMMEDIATELY and login " +
            "will be denied to non-admins until disabled. Whitelisted admins can still log in.");
        public static string MntConfirmExit => S(
            "Terminare la modalità manutenzione?\n\nIl login normale verrà riabilitato subito per tutti i player.",
            "Exit maintenance mode?\n\nNormal login will be re-enabled for all players immediately.");
        public static string MntMissingTable => S(
            "✗ Modalità Manutenzione non installata: esegui prima i file 8. e 9. nella guida install SQL (1.DB-SICURO-login server-client).",
            "✗ Maintenance Mode not installed: run files 8. and 9. in the SQL install guide (1.DB-SICURO-login server-client) first.");
        public static string MntScriptMissing(string script) => S(
            $"✗ Script non trovato in manutenzione\\sql: {script}", $"✗ Script not found in manutenzione\\sql: {script}");
        public static string MntTogglingOn => S("Attivo MaintenanceMode (tabella, auth Windows)...", "Enabling MaintenanceMode (table, Windows auth)...");
        public static string MntTogglingOff => S("Disattivo MaintenanceMode (tabella, auth Windows)...", "Disabling MaintenanceMode (table, Windows auth)...");
        public static string MntSendingNotice => S("Invio notice (account configurato)...", "Sending notice (configured account)...");
        public static string MntNoticeFailed => S("WARN: notice fallito (manutenzione resta comunque applicata).", "WARN: notice failed (maintenance state still applied).");
        public static string MntKicking => S("Kick dei player normali online (account configurato)...", "Kicking normal players online (configured account)...");
        public static string MntKickCount(int n) => S($"{n} player normali online da kickare.", $"{n} normal players online to kick.");
        public static string MntAutoKicked(int n) => S($"🔁 Ri-kick automatico: {n} player normali rimasti online (es. Character Screen) espulsi.", $"🔁 Auto re-kick: {n} normal players still online (e.g. Character Screen) kicked.");

        // Barra di stato persistente nell'header (visibile in ogni vista) — vedi HeaderStatusTick
        public static string HeaderMntBanner => S(
            "🚧 MODALITÀ MANUTENZIONE ATTIVA — login player normali bloccato",
            "🚧 MAINTENANCE MODE ACTIVE — normal player login blocked");
        public static string HeaderSysOk => S("● Sistema OK", "● System OK");
        public static string HeaderSysWarn(bool svcOk, bool fwOk, bool webOk)
        {
            var bad = new List<string>();
            if (!svcOk) bad.Add(S("servizi", "services"));
            if (!fwOk) bad.Add(S("firewall", "firewall"));
            if (!webOk) bad.Add(S("web", "web"));
            string list = string.Join(S(", ", ", "), bad);
            return S($"⚠ Problema: {list}", $"⚠ Issue: {list}");
        }
        public static string MntEnterDone => S("Modalità manutenzione ATTIVA.", "Maintenance mode ACTIVE.");
        public static string MntExitDone => S("Modalità manutenzione terminata.", "Maintenance mode ended.");
        public static string MntActionFailed => S("✗ Azione manutenzione non completata: verifica il log sopra.", "✗ Maintenance action not completed: check the log above.");

        // ── Key Rotation (vista) ─────────────────────────────
        public static string KrTitle => S("Key Rotation", "Key Rotation");
        public static string KrChecking => S("verifica…", "checking…");
        public static string KrTaskNotInstalled => S("Task 04:00 non installato", "04:00 task not installed");
        public static string KrTaskState(bool enabled, Core.TaskLogState last)
        {
            string en = enabled ? S("Task 04:00 abilitato", "04:00 task enabled")
                                : S("Task 04:00 disabilitato", "04:00 task disabled");
            string lr = last == Core.TaskLogState.Error ? S(" · ultimo run: ERRORE", " · last run: ERROR")
                      : last == Core.TaskLogState.Ok ? S(" · ultimo run: OK", " · last run: OK")
                      : "";
            return en + lr;
        }
        public static string KrNextRun(string next) =>
            string.IsNullOrWhiteSpace(next) ? "" : S($"prossimo: {next}", $"next: {next}");
        public static string KrKeyLine(string name, string fp) =>
            fp == null ? S($"{name,-13} — assente", $"{name,-13} — missing")
                       : $"{name,-13} #{fp}";
        public static string KrSahNote => S("(rotazione manuale)", "(manual rotation)");
        public static string KrKeysMeta(DateTime m) => S(
            $"GMSecurity.ini · ultima modifica {m:dd/MM/yyyy HH:mm}",
            $"GMSecurity.ini · last modified {m:MM/dd/yyyy HH:mm}");
        public static string KrIniMissing(string p) => S(
            $"GMSecurity.ini non trovato: {p}", $"GMSecurity.ini not found: {p}");
        public static string KrScriptMissing(string p) => S(
            $"Script di rotazione non trovato: {p}", $"Rotation script not found: {p}");
        public static string KrConfirmRotate => S(
            "Rigenerare ORA le chiavi in PRODUZIONE?\n\nVengono rigenerate ClientSecret/TokenSecret/PatchKeyHex, ri-cifrate le patch e invalidate le sessioni di gioco. Il server NON viene riavviato: dovrai riavviarlo per applicare le nuove chiavi.",
            "Regenerate the keys in PRODUCTION NOW?\n\nClientSecret/TokenSecret/PatchKeyHex are regenerated, patches are re-encrypted and game sessions invalidated. The server is NOT rebooted: you must restart it to apply the new keys.");
        public static string KrRotating => S("Rotazione chiavi in corso", "Rotating keys");
        public static string KrRotateDone => S("✓ Rotazione completata.", "✓ Rotation completed.");
        public static string KrRotateErr(int rc) => S(
            $"✗ Operazione fallita (exit {rc}, vedi log).", $"✗ Operation failed (exit {rc}, see log).");
        public static string KrConfirmInstall => S(
            "Installare/aggiornare il task pianificato 'KeyRotation' (ogni giorno alle 04:00)?\n\nEsegue la rotazione headless via l'Hub e riavvia SOLO i servizi ps_game/ps_login (nessun reboot del server). Disabilita il vecchio task esterno 'ShaiyaKeyRotation-Riavvio' per evitare doppie rotazioni.",
            "Install/update the scheduled task 'KeyRotation' (every day at 04:00)?\n\nRuns the rotation headless via the Hub and restarts ONLY the ps_game/ps_login services (no server reboot). Disable the old external task 'ShaiyaKeyRotation-Riavvio' to avoid double rotations.");
        public static string KrInstalling => S("Installazione task 04:00", "Installing 04:00 task");
        public static string KrInstallDone => S("✓ Task 04:00 installato/aggiornato (rotazione + riavvio servizi).", "✓ 04:00 task installed/updated (rotation + service restart).");
        public static string KrNoLog => S(
            "(nessun log: esegui prima 'Ruota ora' o il task)", "(no log: run 'Rotate now' or the task first)");

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
