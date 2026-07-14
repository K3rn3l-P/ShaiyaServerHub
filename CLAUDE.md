# CLAUDE.md — ShaiyaServerHub (mappa del programma)

> Scopo: farti capire **subito** com'è strutturato ShaiyaServerHub senza rileggere tutto il codice ogni volta.
> **Tienilo aggiornato ad ogni modifica strutturale** (nuova vista/servizio/file Core, rinomina, refactor).
> Complementare a: `README.md` (utente), `PLAN.md` (architettura/roadmap), memoria in
> `~/.claude/projects/…-ShaiyaServerHub/memory/` (`MEMORY.md` = indice). La `CLAUDE.md` del repo padre
> (`..\CLAUDE.md`) contiene infra/SSH/MCP/sicurezza globale.

## Cos'è
App desktop **WPF, .NET Framework 4.8** (`ShaiyaServerHub.exe`): pannello di amministrazione del server
Shaiya (servizi di gioco, task pianificati, script SQL/PS1, web server, firewall, canale comandi PSM_Cmd,
test suite di sicurezza). **Gira SUL server** → `localhost` = server in produzione; SSH solo per dev/deploy.
Vedi memoria [[hub-not-server]].

## Build & deploy
```
powershell -File ShaiyaServerHub\Build-And-Copy.ps1 -Config Release
```
**Chiudere SEMPRE con questo** (build + copia `DefaultScripts\` nell'output + inietta password dal `.env`).
Output pronto per copia: `ShaiyaServerHub\bin\Release\ShaiyaServerHub`. Deploy sul server in
`C:\ShaiyaServer\ShaiyaServerHub\`. Vedi memoria [[build-with-ps1]].

## Layout repo
```
CLAUDE.md  README.md  PLAN.md  .env  ShaiyaServerHub.sln  build-and-copy.bat
ShaiyaServerHub\
  App.xaml(.cs)            avvio; modalità headless task; log globali (vedi sotto)
  MainWindow.xaml(.cs)     shell: nav a sinistra + viste + DASHBOARD; tutti gli handler UI
  Converters.cs            IValueConverter vari per il binding
  app.manifest  icon.ico
  Lang\  it.xaml  en.xaml  ← DIZIONARIO IT/EN (vedi §Localizzazione)
  Styles\ AppStyles.xaml   stili condivisi (NavItem, MiniButton, AccentButton, DangerButton, …)
  Controls\                UserControl riusabili (vedi sotto)
  Core\                    logica non-UI (servizi, modelli, parser, repository)
  DefaultScripts\          script di default copiati nell'output al build
    scripts\{sql,ps1}      area "Script" principale
    functions\psm_cmd-scc\ area "Funzioni" (una sottocartella per scheda)
    testsuite\{sql,ps1}    area "Test Suite" (audit sicurezza 01→NN)
    keyrotation\ps1        area "Key Rotation" (Rotate-ShaiyaKeys.ps1: rotazione chiavi + reload ps_game/ps_login)
    manutenzione\sql       area "Manutenzione" (notice-enter/exit.sql; orchestrazione IN C# —
                           MainWindow.BtnMaintenanceToggle_Click, NON ps1: lettura Chars/Users_Master +
                           UPDATE tabella SEMPRE -E; EXEC usp_RunCommand/usp_SendNotice SEMPRE account
                           configurato (ShaiyaTaskAgent). DDL tabella+gate proc a parte — vedi [[maintenance-mode-dashboard]] [[sql-auth-slash-vs-table]])
```

## Barra di stato persistente (header, visibile in OGNI vista)
Nell'header (sotto il titolo "SHAIYA SERVER HUB", stessa `Border`, sempre presente qualunque vista sia
aperta): banner rosso scuro "🚧 MANUTENZIONE ATTIVA" (solo se `MaintenanceMode.Enabled=1`) + conteggio
player online admin/normal (sempre) + pallino salute sistema (servizi/firewall/web, sempre). Pilotata da
`MainWindow.HeaderStatusTick()` (timer `_headerStatusTimer`, 15s, **indipendente da quale pannello è
visibile** — a differenza delle altre sonde legate a `Visibility`). Stesso timer fa anche il ri-kick
automatico manutenzione. Vedi [[maintenance-mode-dashboard]].

## Viste (nav) — `MainWindow`
RadioButton `NavXxx` con `Tag` → handler `Nav_Checked` → `ShowView(tag)`; navigazione programmatica via
`SelectView(tag)` (tessere/azioni Dashboard). Tag e pannello:

| Tag | Pannello | Contenuto |
|---|---|---|
| `dashboard` | `PanelDashboard` | **vista di default**, panoramica (vedi §Dashboard) |
| `scripts` | `PanelScripts` | script `scripts\sql` + `scripts\ps1` (due sezioni) |
| `newtask` | `PanelNewTask` | form nuovo task pianificato (`NewTaskControl`) |
| `tasks` | `PanelTasks` | lista task `\ShaiyaServerHub\` (Pianificati) |
| `services` | `PanelServices` | servizi di gioco (start/stop) |
| `web` | `PanelWeb` | web server XAMPP/IIS + health/cert |
| `functions` | `PanelFunctions` | schede funzione (PSM_Cmd-SCC, Windows Firewall) |
| `testsuite` | `PanelTestSuite` | test suite sicurezza (`ScriptAreaControl`) |
| `keyrotation` | `PanelKeyRotation` | rotazione chiavi (`KeyRotationControl`): stato task 04:00, "Ruota ora", fingerprint chiavi |
| `ai` | `PanelAi` | AI del server (`AiControl`): chat + feed tool letti — vedi §AI (Hub Brain) |
| `log` | `PanelLog` | log globale UI |

## Core (`ShaiyaServerHub\Core\`)
| File | Ruolo |
|---|---|
| `HubSettings.cs` | impostazioni (SQL server/db/auth, percorsi, canale comandi); `Load()`/`Save()` |
| `ServiceMonitor.cs` | `Query()` stato servizi Windows di gioco (veloce, in-process) |
| `TaskSchedulerService.cs` | `List()`/create/enable/disable/remove task via schtasks; `ScheduledTaskInfo` |
| `TaskXml.cs` `TaskDefinition.cs` `TaskLogging.cs` `EventTriggerBuilder.cs` | costruzione task (trigger MENSILI via RegisterTask da XML — vedi [[monthly-trigger-com-xml]]) |
| `TaskLogReader.cs` | esito ultimo run di un task (`LastRunState`→`Ok/Error/None`) + lettura log |
| `ScriptRepository.cs` | scopre script in `<area>\sql` e `<area>\ps1`; aree `Main`/`TestSuite`/`Function(name)` |
| `ScriptItem.cs` | uno script scoperto; `RunStatus`/`LastLog` + brush badge PASS/WARN/FAIL/ERRORE/INFO |
| `ScriptRunner.cs` | esegue SQL (sqlcmd), PS1 (powershell), BAT (cmd); emette marker `[sqlcmd]/[powershell]/[cmd] <name>` + `RESULT:`/exit |
| `AreaLogParser.cs` | **parser condiviso**: `ParsePerScript(log)`→`{nomeFile:{Status,Log}}` (ultimo run vince). Usato da badge finestra E card Dashboard |
| `TestSuiteReport.cs` | conta PASS/WARN/FAIL per la card Dashboard (usa `AreaLogParser`) |
| `SahCrypt.cs` | crypto ESAH (AES-256-GCM via bcrypt.dll), byte-compat con sahcrypt.exe; chiave sempre esplicita (mai hardcoded). Esposta via CLI `--sah-encrypt/--sah-decrypt` — vedi [[keyrotation-hub-integration]] |
| `KeyRotationReport.cs` | stato rotazione (task 04:00 + ultima rotazione da GMSecurity.ini) per la card Dashboard |
| `MaintenanceReport.cs` | stato modalità manutenzione (PS_UserData.dbo.MaintenanceMode) per la card Dashboard — vedi [[maintenance-mode-dashboard]] |
| `Ai\*.cs` | modulo **AI (Hub Brain)** — vedi §AI (Hub Brain) sotto |
| `CommandChannel.cs` `PsmCmd*.cs` | canale comandi PSM_Cmd (usp_RunCommand); auth Windows forzata — vedi [[funzioni-forced-windows-auth]] |
| `FirewallManager.cs` `FirewallModels.cs` | stato/regole Windows Firewall (+ simplewall) — vedi [[firewall-posture-simplewall]] |
| `WebServer\` | `IWebServer`, `WebProbe` (health/cert TLS), `WebSettingsRepo` (PS_WebSite.dbo.Web_Settings via `Ernoweb@`) |
| `ProcessRunner.cs` | `Run`/`RunCapture` wrapper processi con streaming del log |
| `ExternalToolsManager.cs` | download/stato **Sysinternals autorunsc.exe** da `live.sysinternals.com` (link diretto, MAI winget — non garantito su Windows Server) in `tools\sysinternals\` (fuori git, `.gitignore` `/ShaiyaServerHub/tools/`, stesso trattamento di `.env`); `RunAutorunsBaselineCheck()` esegue `testsuite\ps1\22_autoruns_baseline.ps1` — unica fonte usata da badge Test Suite, tool AI (`autoruns_audit`) e scheduler giornaliero |

## Controls (`ShaiyaServerHub\Controls\`)
- `KeyRotationControl` — **vista Key Rotation**: stato del task pianificato 04:00 (installa/abilita/disabilita),
  "Ruota ora" (esegue `keyrotation\ps1\Rotate-ShaiyaKeys.ps1` via `ScriptRunner`), fingerprint SHA-256 delle
  chiavi correnti (mai in chiaro), "Modifica" ps1. Il **reload dei servizi è NEL ps1** (stop→verifica→5s→start
  di `ps_game`/`ps_login`): stessa logica per "Ruota ora" e per il task 04:00 (`--run`), **niente reboot del
  server**. Il ps1 emette il marker `RIAVVIO-RICHIESTO` solo se un servizio non riparte → banner fallback.
  Modello: `FirewallControl`. Vedi [[keyrotation-hub-integration]].
- `ScriptAreaControl` — **area script riusabile** (Test Suite + Funzioni): lista con badge esito per riga,
  toolbar (Reload/Apri cartelle/Log/Nuovo file), log embedded, slot `Actions` iniettato dall'host.
  `Reload()` → `HydrateOutcomes()` ripopola i badge dal log persistito (via `AreaLogParser`).
- `NewTaskControl` / `CadencePicker` — form task + selettore cadenza (MINUTE/HOURLY/DAILY/WEEKLY/MONTHLY/ONCE/LOGON/BOOT/SHUTDOWN/EVENT).
- `FirewallControl` — scheda Windows Firewall dentro Funzioni.
- `AiControl` — **vista AI**: chat con `AgentBrain` + feed dei tool letti + ⚙ apre le Impostazioni
  (GroupBox "AI (Hub Brain)"). P1 (stato attuale): SOLO tool T0 (osservazione), nessuna azione muta
  lo stato del server. Modello: `KeyRotationControl`/`FirewallControl`.

## Dashboard (vista di default)
4 tessere KPI cliccabili (Servizi · Task tot+pallini ●OK/●errore · Script · Web) + corpo: card Servizi (sx) e
**griglia 2×2** dx (**Manutenzione** · Firewall · PSM_Cmd · **Test Suite** con ●PASS/●WARN/●FAIL) + **card Key
Rotation** full-width (stato task 04:00 + ultima rotazione + esito) + Azioni rapide. La card Web dedicata è
stata **rimossa** (era doppione della tessera KPI "Web" in alto, che resta) e sostituita dalla card
**Manutenzione** (●verde Normale / ●rosso ATTIVA / ●grigio non installata, toggle Attiva/Termina) — vedi
[[maintenance-mode-dashboard]].
**Anti-lag (NON regredire):** dati veloci sincroni + timer 3s; roba pesante (schtasks, probe web/firewall,
`AreaLogParser`, `KeyRotationReport`, `MaintenanceReport`, `Ai.AiReport`) **off-thread**
(`RefreshDashboardHeavyAsync`/`…WebAsync`/`…FirewallAsync`/`…KeyRotAsync`/`…MaintenanceAsync`/`…AiAsync`/`ApplyTestSuiteCard`/`ApplyKeyRotationCard`/`ApplyMaintenanceCard`/`ApplyAiCard`)
con **throttle TTL 20s** (`_dashHeavyAt`); `↻ Aggiorna` forza. Dettagli in [[hub-dashboard-default]].
**REGOLA:** ogni nuova vista/funzione ⇒ valuta una card Dashboard "a colpo d'occhio" e chiedi consiglio su come inserirla.

## Localizzazione — DIZIONARIO IT/EN (obbligatorio)
Il programma è **bilingue IT/EN**. Due meccanismi (vedi [[always-localize-it-en]]):
1. **Stringhe XAML** → chiavi in **entrambi** `Lang\it.xaml` e `Lang\en.xaml`, usate con `{DynamicResource Chiave}`.
   `L.Switch(Lang)` scambia il `ResourceDictionary /Lang/*.xaml` a runtime (bottoni IT/EN in header).
2. **Stringhe di codice** (log, messaggi, testi dinamici) → `Core\L.cs`, helper privato `S("it","en")`
   che ritorna in base a `L.Current`. Es: `public static string DashTsNever => S("mai eseguita","never run");`.

**Ogni nuova stringa visibile va aggiunta in IT+EN.** Mai hardcodare testo UI nel `.xaml`/`.cs`.
Per le combo/selettori: legare per **indice**, non per testo (il testo cambia con la lingua).

## Log prodotti
| File (in `AppContext.BaseDirectory`) | Chi scrive | Contenuto |
|---|---|---|
| `hub.log` | `App.WriteLog(cat,txt)` | solo programma/errori (startup, crash, eccezioni) |
| `hub-tasks.log` | `App.WriteTaskLog` | esecuzione headless task (`--run`) |
| `<area>\<area>.log` (es. `testsuite\testsuite.log`) | `ScriptAreaControl.Log` | output run script dell'area (fonte badge/card) |
| `log\<task>.log` | headless `--log` | log per-task opzionale ("Genera log") |

## Modalità headless (Task Scheduler)
`ShaiyaServerHub.exe --run "<script>" [--log "<path>"] [--auth win|sql]` → esegue senza UI, `exit N`
(`App.RunHeadless`). Inoltre CLI crypt (rimpiazza sahcrypt.exe, usata dal ps1 di rotazione):
`--sah-encrypt|--sah-decrypt <in> <out> <64hex>` → ESAH via `Core.SahCrypt`, `exit 0/1/2` (0 ok · 1 uso/IO · 2 crypto). Guardia [[hub-exe-cmd-redirect-zombie-lock]]: l'exe del Hub va usato SOLO con `--run`
(senza, partirebbe la UI in background → task appeso, log lockato).

## AI (Hub Brain)
Modulo che dà all'Hub un'intelligenza (LLM via **OpenRouter**, tool-calling) per osservare il server e
rispondere in chat. Piano completo (autonomia a tier T0-T3, escalation, difesa, roadmap P1→P3) in
`~\.claude\plans\ok-valutiamo-di-inserire-fluttering-rossum.md` — **stato attuale: P1 implementato**
(solo osservazione, nessuna azione muta lo stato del server).

**Layout modulare** (`ai\` accanto all'exe, copiato da Build-And-Copy come le altre aree — nessuna riga
dedicata nello script, la copia `DefaultScripts\**` è già ricorsiva):
```
ai\config\        models.json (slot routine/complex/reasoning + budget) · policy.json (scheletro P2, non ancora letto)
ai\instructions\  system.md (persona) · rules.md (mirror invarianti, promemoria) · playbooks\ (P2+)
ai\knowledge\     server-facts · databases · accounts · commands · defenses · scripts (.md, dati dalla
                   ricognizione reale del server) · learned\ (l'AI scriverà qui in P2+)
ai\scripts\       sql\ ps1\ (riservato P3, AI-generati)
ai\sessions\      log chat/azioni (riservato P2+)
```
Tutti editabili senza ricompilare. `AiPaths.EnsureFolders()` le crea all'avvio (`App.Application_Startup`,
come `ScriptRepository.EnsureAll()`).

**Core\Ai\**:
| File | Ruolo |
|---|---|
| `IAiTool.cs` | contratto capability (Name/Description/Tier/Reversible/ParametersJsonSchema/Execute) |
| `ToolRegistry.cs` | scopre le `IAiTool` via **reflection** — whitelist automatica, un tool nuovo va SOLO aggiunto come classe, nessun'altra modifica (vedi §Estensibilità sotto) |
| `Tools\*.cs` | tool T0 concreti: `services_status`, `firewall_status`, `network_port_audit` (porte reali in ascolto+rischio, fonte autorevole per esposizione — `firewall_status` mostra solo regole `Shaiya-*`/SSH), `web_health`, `testsuite_status` (con `detail`/`script`: quali controlli sono WARN/FAIL + log completo di uno), `scheduled_tasks_status`, `sql_read_query`, `list_game_commands`, `read_web_server_file` (sotto `HubSettings.XamppRoot`, redazione automatica pattern-secret), `read_hub_file` (sotto la root del Hub stesso — `scripts\`/`log\`/`ai\`, blocklist assoluta `hub.settings.xml`+`.env`), `read_service_log` (log di sistema PIÙ RECENTE di un `ps_*`, nuovo file ad ogni avvio — diagnosi prima/dopo un riavvio), `connection_census` (connessioni TCP Established per IP su porte gioco), `security_telemetry` (query fisse su SecurityEvents/LoginAttempts/GameSecuritySessions), `web_search` — ognuno wrappa codice esistente (`ServiceMonitor`, `FirewallManager`, `WebProbe`, `TestSuiteReport`/`AreaLogParser`, `TaskSchedulerService`/`TaskLogReader`). Tool T1 (mutano stato, binario via `PolicyGate.Authorize`): `restart_crashed_service` (un servizio) e `heal_game_services` (TUTTI i servizi di gioco fermi, ordine di dipendenza corretto via `ServiceMonitor.GameServiceStartOrder`, con stabilizzazione tra uno e l'altro). Tool T2 (via `PolicyGate.Evaluate`+`EscalationEngine`): `kick_player` (4 varianti kick, riusa `usp_RunCommand` come `MainWindow.AutoKickAllNormals` — può eseguire subito, andare in coda con scadenza, o richiedere approvazione Telegram a seconda di urgency/confidence dichiarati; **verifica sempre l'esito REALE leggendo il log `ps_game`** dopo l'EXEC, mai fidarsi del solo `rc=0` SQL — vedi `Core\ServiceLogReader.cs`, bug reale trovato: `/kickun` con nome-personaggio invece di username tornava rc=0 ma il server loggava "failed") |
| `Invariants.cs` | regole HARD statiche (P2 Blocco 1): whitelist servizi riavviabili (`ps_game/ps_login/ps_session/ps_gameLog/ps_userLog/ps_dbAgent`), elenco protetti espliciti (`MSSQLSERVER/sshd/PSM_AgentServer/Client`), kill-switch (file `ai\STOP`) |
| `PolicyGate.cs` | `Authorize(...)` per T1 (binario, invariato) + `Evaluate(...)` per T2+ (passa da `EscalationEngine` → Denied/ExecuteImmediately/Queued). Stessi controlli base (kill-switch/master-enable/rate-cap/invarianti per nome tool) prima di entrambi. Log `hub.log` categoria `AI-POLICYGATE` |
| `EscalationEngine.cs` + `PolicyConfigFile.cs` | formula a punteggio (R/H/C/B) da `ai\config\policy.json` (finalmente letto, era scaffoldato in P1): irreversibile/server-wide→sempre approvazione; critico+confidenza alta→contenimento immediato; confidenza bassa→sempre approvazione; altrimenti grace dinamico `clamp(Tbase/(C·w(H)·u(B)))` |
| `ActionQueue.cs` | coda in-memory azioni T2+ pendenti (non persistita su riavvio), risoluzione atomica (niente doppie esecuzioni) |
| `TelegramClient.cs` + `Notifier.cs` | bot 2-vie: messaggi con bottoni ✅Approva/❌Nega/🙋Prendo io + polling callback (diverso da `TelegramHelper.cs`, quello resta solo per rilevare il Chat ID in Impostazioni) |
| `ApprovalWatchdog.cs` | timer 7s, vive quanto il processo Hub (avviato da `AiControl.Init`): risolve i callback Telegram (bottoni), esegue le azioni con grace scaduta senza risposta, **e inoltra i messaggi di testo libero Telegram** ad `AgentBrain.Chat` (chat 2 vie completa dal telefono, non solo Approva/Nega) — solo dalla chat configurata (`TelegramChatId`), su `Task.Run` separato per non bloccare il tick. Instrada anche i **comandi slash** (`/db_audit` ecc., vedi `TelegramCommands`): `/help` statico, comando noto → prompt precompilato espanso (stessa pipeline/gate di un messaggio libero), sconosciuto → avviso senza sprecare un turno LLM. Registra il menu comandi (`setMyCommands`) al primo tick utile |
| `TelegramCommands.cs` | elenco comandi slash del bot (nome vincolato dalla Bot API: solo `[a-z0-9_]`, niente trattini) → prompt precompilato per area (db_audit/web_audit/autostart/services/network/security/tasks/disk/test_suite) — scorciatoia di FORMULAZIONE, non bypassa `PolicyGate`: il prompt passa dalla stessa `AgentBrain.Chat` di un messaggio libero |
| `SharedChatSession.cs` | storico chat CONDIVISO tra Hub UI e Telegram (richiesta esplicita: una domanda fatta da un canale deve essere visibile dall'altro) — un solo turno alla volta su tutto il Hub (`TryBeginTurn`/`EndTurn`), NON riguarda i giri proattivi (WatchLoop/SecurityDigestScheduler/AutorunsAuditScheduler restano indipendenti, prompt sintetico senza history) |
| `TelegramFormatter.cs` | markdown leggero (stesso stile di `MarkdownLite`, ma senza dipendenza WPF) → HTML Telegram (`<b>/<i>/<code>/<pre>`, escape solo `&<>`) — bug reale corretto: MarkdownV2 di Telegram richiede l'escape di 18 caratteri speciali fuori dai code block, un carattere non escapato in prosa normale rompe l'INTERO messaggio (verificato via ricerca web); usato da `Notifier.NotifyInfo` per tutto il testo generato dall'LLM (digest, esiti, risposte chat Telegram) |
| `WatchLoop.cs` | timer 3 min (avviato da `AiControl.Init`, stesso processo per evitare due `PolicyGate` indipendenti): pre-check economico `ServiceMonitor.Query` (no LLM), solo se trova servizi gioco fermi invoca `AgentBrain.Chat` con prompt sintetico — sweep proattivo, l'AI agisce anche senza chat esplicita |
| `SecurityDigestScheduler.cs` | timer 15 min (grazia 30 min dall'avvio Hub): controlli DIRETTI in C# (servizi/circuit-breaker/`security_telemetry`/test suite/task falliti/**SQL Audit tailing**, zero costo LLM) — invoca `AgentBrain.Chat` e notifica Telegram SOLO se trova un'anomalia reale, mai un digest "tutto ok" |
| `AutorunsAuditScheduler.cs` | timer (non-periodico, si ri-schedula da sé ad ogni tick) che controlla se è trascorso l'intervallo configurato dall'ultimo giro: esegue `ExternalToolsManager.RunAutorunsBaselineCheck` (autorunsc vs baseline) — colma il gap "config-drift"; notifica Telegram SOLO su drift NUOVO (dedup per hash del testo, non ripete lo stesso avviso ogni giorno); no-op se autorunsc non è scaricato. **Intervalli configurabili** (`HubSettings.AiAutorunsCheckIntervalMinutes`/`RunEveryHours`/`StartupGraceMinutes`, default 30min/24h/30min) da Impostazioni → scheda AI → gruppo "Autorun / persistenza" — cambia dal giro successivo, nessun riavvio Hub necessario |
| `PolicyGate.cs` (circuit breaker) | sliding window tentativi T1+ (`ai\config\policy.json` sezione `circuitBreaker`) — STICKY, resta aperto finché non resettato a mano (bottone Impostazioni AI → "Sicurezza — Circuit Breaker"), mai auto-richiusura |
| `OutcomeTracker.cs` | log persistito (`ai\knowledge\learned\outcomes.log`, sopravvive a riavvio) di ogni esito T1+/T2 (Denied/Executed/Approved/Denied/TakenOver/AutoExecuted) — raccolta dati per un futuro shadow-mode, non ancora enforcement |
| `AiSqlReader.cs` | percorso SQL **separato** per l'AI: login dedicato `ShaiyaAiReader` (mai le credenziali del Hub), guardia lato client SELECT-only oltre ai permessi lato server |
| `PlayerLookup.cs` | risolve `target` (nome/username/CharID/UserUID) contro `PS_GameData.dbo.Chars` via `ShaiyaAiReader` — riusato da `kick_player`/`ban_player`, mai un'ipotesi del modello |
| `AiBanExecutor.cs` | esecutore T3 `ban_player`: `EXEC sp_BanUser` via login dedicato `ShaiyaAiBanExecutor` (SOLO EXECUTE su quella procedura), fail-closed se password non configurata |
| `Sensors\ConnectionCensus.cs` | connessioni TCP realmente `Established` per IP remoto sulle porte di gioco (30800/30810 default, `Get-NetTCPConnection`) — fonte primaria flood/abuso |
| `Sensors\SecurityTelemetry.cs` | query T-SQL FISSE (non generate dal modello) su `SecurityEvents`/`LoginAttempts`/`GameSecuritySessions` via `AiSqlReader` — schema verificato via SSH |
| `Finding.cs` | POCO `{Severity, Category, Text}` — gravità Low/Medium/High deterministica assegnata dal codice, mai dal modello (P4) |
| `Sensors\WindowsEventLog.cs` | Event Log Windows via `Get-WinEvent` (stesso pattern di `EventTriggerBuilder`): login OS falliti 4625/4740 (filtra rumore interno senza IP), riavvii/crash 6008/41/1001 (filtra "Hypervisor launch failed"), crash servizio 7031/7034/7024 — tutti i filtri verificati sul server reale via SSH |
| `Sensors\DiskSpaceSensor.cs` | spazio disco volumi fissi via `Win32_LogicalDisk` |
| `Sensors\SqlHealthSensor.cs` | blocking/tempdb/query lunghe via DMV (`AiSqlReader`, login ha già `VIEW SERVER STATE`) |
| `TempBlockRegistry.cs` | stato in-memory IP bloccati temporaneamente (scadenza+motivo) — fonte di verità per l'UNICA regola firewall dinamica |
| `TempBlockScheduler.cs` | timer 30s, rimuove blocchi scaduti + risincronizza la regola — SEMPRE attivo indipendente da `AiMasterEnabled` (rimuovere è sempre sicuro) |
| `Tools\WriteLearnedNoteTool.cs` | T1 `write_learned_note`: scrive/aggiunge prosa in `ai\knowledge\learned\<topic>.md`, riletta da `KnowledgeStore` nei turni futuri — chiude il loop apprendimento (prima `OutcomeTracker.outcomes.log` non veniva mai riletto, `.log` escluso da `KnowledgeStore`) |
| `Tools\SecretRedactor.cs` | redazione riga-per-riga CONDIVISA da tutti i tool `read_*_file` — regex rinforzata (nome campo contiene key/secret/hash/token/password come sottostringa ovunque, non solo con prefisso api_/private_ — bug reale corretto: non catturava `TokenSecret=`/`SahKeyHex=`) |
| `Tools\ReadServerFileTool.cs` | T0 `read_server_file`: lettura sola dell'albero `C:\ShaiyaServer` (GMSecurity.ini con IP_WHITELIST/ANTI_TAMPER visibili ma segreti crittografici SEMPRE redatti, EtainShield.ini) — scoped via `ServerBinPath` |
| `Tools\AutorunsAuditTool.cs` | T0 `autoruns_audit`: scansione LIVE autostart Windows (Sysinternals autorunsc) vs baseline salvato — colma il gap "config-drift"/persistenza mai coperto dai 21 controlli Test Suite (infra-security, non ASEP). Riusa `ExternalToolsManager.RunAutorunsBaselineCheck` (stesso script/baseline del badge Test Suite e dello scheduler giornaliero) |
| `Sensors\SqlAuditSensor.cs` | legge il **SQL Server Audit nativo** (`ShaiyaHub_LoginAudit`/`ShaiyaHub_SchemaChangesAudit`, installati da `testsuite\sql\23_sql_audit_install.sql`, `TO APPLICATION_LOG`, EventID 33205 provider `MSSQLSERVER` — verificato sul server reale, differenziato dal campo `action_id`: CR/AL/DR=modifica schema, LGIF=login fallito) — config-drift del DB (stesso principio di autorunsc ma per schema SQL) + login SQL falliti a livello SERVER (diverso da `security_telemetry`, che vede solo login di gioco). `TailNewFindings` usa un marker persistito (`ai\sqlaudit-lastcheck.txt`) invece di una finestra fissa: un evento non viene mai segnalato due volte nel digest |
| `Tools\SqlAuditTool.cs` | T0 `sql_audit` (focus schema_changes/failed_logins/summary + minutes) — wrapper on-demand su `SqlAuditSensor` |
| `OpenRouterClient.cs` | `HttpClient`+`System.Text.Json` verso OpenRouter, chat completions + tool-calling; fallback **nativo lato server** via parametro `models` (array, un'unica richiesta HTTP — [docs](https://openrouter.ai/docs/guides/routing/model-fallbacks)), non retry client-side; retry/backoff solo per errori di trasporto (429/5xx) |
| `ModelRouter.cs` + `ModelsConfigFile.cs` | 3 slot in `ai\config\models.json`: **Critical**=`openrouter/auto` (Auto Router, credito vero, incidenti reali); **Default**/**Fallback** auto-aggiornati da `OpenRouterModelCatalog` (top-2 free+tool-calling per rank-sum popolarità/velocità, TTL 24h, MAI `openrouter/free` come primo paracadute — quello resta 3° livello hardcoded in `AgentBrain`); editabili da file O da Settings→scheda AI (checkbox auto-update + "🔄 Verifica ora"); budget richieste/giorno-ora |
| `KnowledgeStore.cs` | concatena `ai\knowledge\*.md` (+ `learned\`) nel contesto |
| `AgentBrain.cs` | loop ragiona→tool→ragiona; system prompt = `instructions\system.md`+`rules.md`+catalogo tool+knowledge; SOLO tool noti a `ToolRegistry` eseguibili (whitelist) |
| `AiReport.cs` | stato per la card Dashboard (pattern `MaintenanceReport`) |

**Estensibilità (come far "vedere" all'AI una nuova funzione del Hub):** creare una classe pubblica che
implementa `IAiTool` con un costruttore `(AiToolContext ctx)` — `ToolRegistry` la scopre da sola via
reflection all'avvio. Nessuna registrazione manuale altrove.

**Impostazioni AI**: `SettingsWindow` **a schede** (Server · Web · Canale comandi · AI — non più tutto in
verticale). Scheda AI: API key OpenRouter cifrata DPAPI, 3 slot modello, bot Telegram con mini-guida
BotFather in-UI, login SQL `ShaiyaAiReader`, URL SearXNG. Campi in `HubSettings.cs` seguono lo stesso
pattern DPAPI di `SqlPasswordEnc`. Login `ShaiyaAiReader` creato con lo script
`3.0.Login-ReadOnly-AI-Monitor.sql` (anche sulla guida NAS `2.Guida-Rule-MSSQL\`, per reinstall futuri).

**SearXNG (tool `web_search`)**: default = bridge **MCP** (`AiWebSearchMode="Mcp"`, `McpSearxngClient.cs`
— JSON-RPC su HTTP, stesso canale/istanza usato da Claude Code in questa sessione), non REST diretto —
modalità `"Rest"` disponibile per una futura istanza propria con `/search?format=json` (SearXNG lo
disabilita per default → 403 finché non si abilita `search: formats: [html, json]` in `settings.yml`).
`time_range` opzionale (day/month/year). **Motori sospesi → zero risultati**: l'istanza condivisa
(`10.10.19.4`, CasaOS) auto-sospende motori dopo burst di ricerche (cooldown 1h-24h, by-design SearXNG,
non un bug) — `WebSearchTool` ritenta automaticamente forzando `engines="startpage,bing,mojeek"` quando
il set di default dà zero risultati (verificato funzionare anche a infra degradata). Vedi memoria
[[web-search-engines-fallback]] e `~\.claude\CLAUDE.md` per i dettagli/diagnosi completa.

**🎉🎉 P1→P2→P3 del piano "Hub Brain" originale è COMPLETO** (P2: 5/5 blocchi · P3: 6/6 blocchi, tutti
verificati dal vivo — dettaglio completo nel piano). Tier finali: **T0** (~15 tool osservazione, incl.
`connection_census`/`security_telemetry`/`read_hub_file`) · **T1** (`restart_crashed_service`,
`heal_game_services`, `unblock_ip`, `write_ai_test_script`, `write_learned_note` — binario via
`Authorize`) · **T2** (`kick_player`, `temp_block_ip` — via `Evaluate`/`EscalationEngine`, coda/
immediato/grace) · **T3** (`ban_player`, `run_scheduled_task` — `Reversible=false`, sempre approvazione
Telegram per costruzione). Playbook di risposta incidenti in `ai\instructions\playbooks\*.md`
(flood-abuso/bruteforce-login/tamper-speedhack), auto-apprendimento in `ai\knowledge\learned\*.md`
(scritto da `write_learned_note`, riletto da `KnowledgeStore`). `--ai-watch` CLI headless resta
deliberatamente rimandato (`WatchLoop` in-process copre già "Hub aperto sul server"). `read_server_file`
(T0, `C:\ShaiyaServer`) legge anche `GMSecurity.ini`/`EtainShield.ini` con redazione automatica dei
segreti crittografici reali (`SecretRedactor.cs`, condiviso da tutti i tool `read_*_file`).
`SecurityDigestScheduler` (timer 15 min, grazia 30 min all'avvio) colma "digest+baselining" del piano
originale — alert solo su anomalia, zero costo LLM su giro pulito. **P4** (su richiesta esplicita —
"controlla TUTTO Windows Server mentre sono lontano ore/mezze giornate") estende lo stesso
`SecurityDigestScheduler` con sorveglianza OS/SQL/web completa: nuovo modello di GRAVITÀ (`Finding.cs`,
Low/Medium/High deterministico in C#, mai deciso dal modello) — sotto soglia (`AiDigestTelegramMinSeverity`,
default Medium) solo audit log, sopra soglia turno LLM+Telegram (`Notifier.NotifyDigest`). Nuovi sensori
`WindowsEventLog.cs` (login OS falliti RDP/SSH 4625/4740, riavvii/crash inattesi 6008/41/1001, crash
servizio 7031/7034/7024 — tutti con filtri per falsi-positivi noti VERIFICATI sul server reale via SSH
prima di scrivere il codice, es. rumore Kerberos interno MSSQLSERVER sui 4625 e "Hypervisor launch
failed" sui 41), `DiskSpaceSensor.cs`, `SqlHealthSensor.cs` (blocking/tempdb/query lunghe, login
`ShaiyaAiReader` aveva GIÀ `VIEW SERVER STATE`, nessun nuovo grant SQL servito) + riuso diretto di
`WebProbe`/`WebServerFactory` per la salute del sito. 3 nuovi tool T0 on-demand (`windows_event_log`/
`disk_space`/`sql_health`) + 3 playbook (`rdp-bruteforce-os`/`unexpected-reboot-crash`/
`disk-space-critico`). CPU/RAM deliberatamente esclusi (rumorosi senza baseline storico). Restano solo,
come lavoro futuro non pianificato in dettaglio: `--ai-watch`, le altre "funzioni future" del piano
originale (config-drift, backup guardian, economy watchdog).

## Convenzioni progetto
- **Sicurezza produzione**: client untrusted, nessun segreto nel client — vedi `..\CLAUDE.md`. Il Hub è lato server.
- **Account SQL**: `Ernoweb@`=sito/PS_WebSite; `ShaiyaTaskAgent`=PSM_Cmd/usp_RunCommand — vedi [[sql-accounts-split]].
- **Ricerca MCP** su best-practice/CVE per fix di sicurezza (searxng) — vedi `~\.claude\CLAUDE.md`.
- **Script `.ps1`: SOLO ASCII, mai em-dash/accenti** (commenti inclusi) — Windows PowerShell 5.1 legge i
  `.ps1` con la codepage di sistema salvo BOM esplicito, un file UTF-8-senza-BOM con caratteri non-ASCII
  in una stringa viene corrotto a runtime (bug reale: em-dash dentro una stringa ha rotto il parser,
  "hash literal incomplete"). Usa `e'`/`piu'` invece di `è`/`più`, `-` invece di `—`.
- **Mantieni aggiornati**: questo file, `README.md`, `PLAN.md` e la memoria ad ogni nuova funzione o refactor.
