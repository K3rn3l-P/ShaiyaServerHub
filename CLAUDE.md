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
| `CommandChannel.cs` `PsmCmd*.cs` | canale comandi PSM_Cmd (usp_RunCommand); auth Windows forzata — vedi [[funzioni-forced-windows-auth]] |
| `FirewallManager.cs` `FirewallModels.cs` | stato/regole Windows Firewall (+ simplewall) — vedi [[firewall-posture-simplewall]] |
| `WebServer\` | `IWebServer`, `WebProbe` (health/cert TLS), `WebSettingsRepo` (PS_WebSite.dbo.Web_Settings via `Ernoweb@`) |
| `ProcessRunner.cs` | `Run`/`RunCapture` wrapper processi con streaming del log |

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

## Dashboard (vista di default)
4 tessere KPI cliccabili (Servizi · Task tot+pallini ●OK/●errore · Script · Web) + corpo: card Servizi (sx) e
**griglia 2×2** dx (**Manutenzione** · Firewall · PSM_Cmd · **Test Suite** con ●PASS/●WARN/●FAIL) + **card Key
Rotation** full-width (stato task 04:00 + ultima rotazione + esito) + Azioni rapide. La card Web dedicata è
stata **rimossa** (era doppione della tessera KPI "Web" in alto, che resta) e sostituita dalla card
**Manutenzione** (●verde Normale / ●rosso ATTIVA / ●grigio non installata, toggle Attiva/Termina) — vedi
[[maintenance-mode-dashboard]].
**Anti-lag (NON regredire):** dati veloci sincroni + timer 3s; roba pesante (schtasks, probe web/firewall,
`AreaLogParser`, `KeyRotationReport`, `MaintenanceReport`) **off-thread**
(`RefreshDashboardHeavyAsync`/`…WebAsync`/`…FirewallAsync`/`…KeyRotAsync`/`…MaintenanceAsync`/`ApplyTestSuiteCard`/`ApplyKeyRotationCard`/`ApplyMaintenanceCard`)
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

## Convenzioni progetto
- **Sicurezza produzione**: client untrusted, nessun segreto nel client — vedi `..\CLAUDE.md`. Il Hub è lato server.
- **Account SQL**: `Ernoweb@`=sito/PS_WebSite; `ShaiyaTaskAgent`=PSM_Cmd/usp_RunCommand — vedi [[sql-accounts-split]].
- **Ricerca MCP** su best-practice/CVE per fix di sicurezza (searxng) — vedi `~\.claude\CLAUDE.md`.
- **Mantieni aggiornati**: questo file, `README.md`, `PLAN.md` e la memoria ad ogni nuova funzione o refactor.
