# ShaiyaServerHub — Piano progetto / ripresa sviluppo

> Console locale di automazione e controllo del server Shaiya. **by K3rn3l**.
> Documento per riprendere il progetto e pianificare nuove implementazioni.
> Ultimo aggiornamento: 2026-07-04 (trigger MONTHLY via COM RegisterTask/XML + guardia anti-zombie cmd-redirect).

---

## 1. Scopo

Sostituire l'esecuzione **manuale** degli script in
`\\10.10.19.2\...\A-Best-Installation-GUIDE\5.Script-Server AUTO` (SQL/PS1/BAT) e la creazione a
mano dei Task Scheduler, con un'unica app desktop. **Uso solo locale, direttamente sul server**
(mai esposta in rete — vale la regola progetto "client untrusted", ma qui è un tool admin locale).

Obiettivo finale: poter installare/gestire un server da zero e automatizzare tutti i job ricorrenti
(EXP notice, boss auto, backup DB, pulizia log, watchdog, avvio servizi) con toggle e un click.

---

## 2. Stack & scelte architetturali (perché)

| Scelta | Valore | Perché |
|---|---|---|
| Linguaggio/UI | **C# + WPF, .NET Framework 4.8** | Coerente con PatchMaker (stesso tema riusabile), gira su Windows Server senza runtime extra |
| csproj | **SDK-style** (`<UseWPF>`+`<UseWindowsForms>`) | Auto-include file, niente lista `<Compile>` manuale |
| Dipendenze | **Zero esterne** | Shella verso `sqlcmd`, `powershell`, `schtasks`, `net`, `ServiceController`. Niente NuGet da gestire |
| Config | `hub.settings.xml` (XmlSerializer) | **GOTCHA:** `System.Web.Extensions`/`JavaScriptSerializer` trascina `System.Web` che il markup compiler WPF (MC1000) NON risolve → build rotta. XmlSerializer è built-in e non ha il problema |
| Password SQL | **DPAPI scope LocalMachine** | Cifrata a riposo; LocalMachine così anche un task sotto SYSTEM può decifrarla |
| Elevazione | `app.manifest` **requireAdministrator** | Start/Stop servizi Windows e `schtasks` richiedono admin → UAC all'avvio |
| Tema | dark/gold `#E8C881`, chrome custom (`WindowStyle=None`) | Copiato/esteso da `shaiya-legacy-updater\PatchMaker\Styles\AppStyles.xaml` |
| Lingua | IT/EN via `Lang/it.xaml`+`en.xaml` + `L.cs` | Toggle in header, come PatchMaker |

---

## 3. Struttura file

```
ShaiyaServerHub\
  ShaiyaServerHub.sln
  README.md · PLAN.md
  ShaiyaServerHub\
    ShaiyaServerHub.csproj      (net48, WinExe, icon.ico, app.manifest)
    app.manifest                (requireAdministrator)
    icon.ico                    (copiata da PatchMaker)
    App.xaml / App.xaml.cs      (avvio UI + modalità headless --run + log globale + crash handler)
    MainWindow.xaml / .cs       (nav laterale + viste + template riga script condiviso + editor/nuovo file)
    EditorWindow.xaml / .cs     (editor file modale sql/ps1/bat: apri/modifica/salva, preserva codifica, Ctrl+S)
    GuideWindow.xaml / .cs      (guida SOLO spiegazione: riavvio server + trigger evento + come usare il generatore)
    EventGenWindow.xaml / .cs   (generatore trigger evento: cerca/scegli servizio da lista → filtro XPath live)
    NewTaskWindow.xaml / .cs    (modale che ospita NewTaskControl — Nuovo task / Edit)
    TriggerDialog.xaml / .cs    (modale "aggiungi/modifica trigger": ospita un CadencePicker → ScheduleSpec)
    Controls\
      NewTaskControl.xaml/.cs   (form task riusabile: trigger principale + lista trigger extra, azione, ☑ log, ❓ guida)
      CadencePicker.xaml/.cs    (selettore cadenza/trigger riusabile: combo+orario/giorni/MONTHLY(giorni-del-mese+mesi)/EVENT+🧩 genera evento)
      ScriptAreaControl.xaml/.cs(area script riusabile Funzioni/Test Suite + editor/nuovo file per riga; badge PASS/WARN/FAIL per riga idratati da log via AreaLogParser in Reload)
    Core\AreaLogParser.cs         (parse per-script del log d'area → esito+blocco ultimo run; usato da badge finestra E card Dashboard/TestSuiteReport)
    SettingsWindow.xaml / .cs   (⚙ config SQL/percorsi/canale comandi)
    L.cs                        (localizzazione: switch dizionari + stringhe di codice)
    Styles\AppStyles.xaml       (tema: GroupBox/Button/TextBox/ComboBox/RadioButton/NavItem/ScrollBar globale)
    Lang\it.xaml · en.xaml      (etichette UI via DynamicResource)
    Core\
      HubSettings.cs            (config + DPAPI, hub.settings.xml)
      ScriptItem.cs             (modello script + badge colore per tipo)
      ScriptRepository.cs       (scopre sql\ e ps1\ accanto all'exe; le crea all'avvio)
      ProcessRunner.cs          (Process con stdout/stderr live → callback)
      ScriptRunner.cs           (sqlcmd / powershell / cmd)
      ScheduledTaskInfo.cs      (modello task + ScheduleSpec: cadenza + EventChannel/EventXPath + MonthDays/Months)
      TaskSchedulerService.cs   (wrapper schtasks: Create/List/Enable/Disable/Delete/RunNow + EVENT + wrapper log;
                                 MONTHLY via CreateViaXml → COM Schedule.Service.RegisterTask da XML completo)
      TaskLogging.cs            (log opzionale task: sqlcmd -o / wrapper cmd redirect; path log\<nome>.log; IsHubExe guard)
      TaskXml.cs                (parse XML task per Edit: cadenza, EVENT via Subscription/QueryList, MONTHLY/ScheduleByMonth)
      EventTriggerBuilder.cs    (XPath/QueryList per trigger evento + scan servizi da eventi 7036 SCM)
      ProcessRunner.cs          (Process live; RunPowerShellEncoded per quoting robusto via -EncodedCommand)
      ServiceMonitor.cs         (ServiceController: Query/Start/Stop/StartAll)
      CommandChannel.cs         (registra login in PSM_Cmd.GmCallerProfile tier ECONOMY)
  (a runtime, accanto all'exe:)
    sql\   ps1\   hub.settings.xml   hub.log
```

---

## 4. Come funziona (runtime)

- **All'avvio** `App.Application_Startup` → `ScriptRepository.EnsureFolders()` crea `sql\` e `ps1\`
  accanto all'exe. L'utente ci mette gli script (vecchi e nuovi) → ricaricati ad ogni apertura.
- **Discovery**: `sql\` → `*.sql`; `ps1\` → `*.ps1/*.bat/*.cmd` (ricorsivo).
- **Nav laterale** (RadioButton `NavItem`) scambia le viste (Visibility toggle in `ShowView`):
  **Dashboard** (default) · Script · Nuovo task · Pianificati · Servizi · Web · Funzioni · Test Suite · Log.
  Auto-switch: Esegui/Start→Log, Crea task→Pianificati. Dalla Dashboard le tessere/azioni chiamano
  `SelectView(tag)` che checka la RadioButton relativa.
- **Dashboard** (`PanelDashboard`, **vista di default** all'avvio): panoramica a colpo d'occhio.
  4 **tessere KPI** cliccabili (Servizi `X/Y` · Task tot + pallini ●OK/●errore · Script tot+`SQL·PS1` · Web ONLINE/OFFLINE)
  + corpo: card Servizi di gioco (mini-lista, sx) e **griglia 2×2** dx = Web health/cert · Sicurezza-Firewall 🟢🟠🔴 ·
  PSM_Cmd 🟢🔴 · **Test Suite** (esiti ultimo run ●PASS/●WARN/●FAIL) + barra **Azioni rapide**.
  La card Test Suite legge `testsuite\testsuite.log` via `TestSuiteReport.Load()` (esito **per-script**: marker
  `[sqlcmd]/[powershell]/[cmd] <name>` + `RESULT:`/`Status :` + `exit N`, ultimo run vince — NON dipende dal
  riassunto `TOTALE:`, così funziona anche dopo run singoli; FAIL rosso = FAIL+ERRORE); la tessera Task usa `LastRunState`.
  **Performance/anti-lag** (§10): i dati veloci (`ServiceMonitor`, timer 3s) sono sincroni; i pesanti
  (`schtasks` List + `LastRunState`, `Discover`, probe web/firewall PowerShell, `TestSuiteReport.Load`) girano **off-thread**
  (`RefreshDashboardHeavyAsync`/`…WebAsync`/`…FirewallAsync` + `ApplyTestSuiteCard`) con **throttle TTL 20s** (`_dashHeavyAt`):
  rientrare nella vista NON rifà i probe (i TextBlock tengono l'ultimo valore → apertura istantanea);
  `↻ Aggiorna` forza (`RefreshDashboard(true)`).
  > **Convenzione:** ogni nuova finestra/scheda/funzione del Hub deve valutare una card Dashboard (KPI / card /
  > slot griglia) e concordarne la forma — Dashboard = punto di sintesi del server.
- **Vista Script**: due sezioni separate **DATABASE—SQL** (blu) e **SISTEMA—PS1/BAT** (verde),
  badge colorato per tipo (`ScriptItem.KindBrush`/`KindBadgeBg`: SQL blu, PS1 verde, BAT arancio).
- **Esegui** → `ScriptRunner`: SQL via `sqlcmd`, PS1 via `powershell -File`, BAT via `cmd /c`. Log live.
- **Nuovo task** → form `NewTaskControl` (riusabile): usato sia nella vista Nuovo task sia nel
  **modale** aperto da "▶ Pianifica" (riga script → `NewTaskWindow` con script preselezionato).
  Logica creazione condivisa (`TaskSchedulerService.Create`) → zero duplicazione.
  I task vivono in una **cartella dedicata** del Task Scheduler: `Task Scheduler Library\ShaiyaServerHub`
  (cartella di primo livello, sorella di `Microsoft`; via `/TN "\ShaiyaServerHub\<nome>"`). Azione =
  `ShaiyaServerHub.exe --run "<scriptFullPath>"` (**headless**, App intercetta `--run` → `RunHeadless` →
  ScriptRunner, no finestra, exit code propagato). Girano come **SYSTEM** `/RU SYSTEM /RL HIGHEST`
  (max privilegio, nessun problema permessi; ma per gli script canale-comandi SYSTEM va in GmCallerProfile).
- **Editor file** (`EditorWindow`): da ogni riga script (**✎ Modifica**) o toolbar (**＋ Nuovo file**, con
  `SaveFileDialog`) si apre/crea un file e lo si modifica. Salva con `💾`/**Ctrl+S**, **preserva la codifica**
  originale (rileva BOM UTF-8/UTF-16, default UTF-8 senza BOM), normalizza EOL a CRLF, conferma se ci sono
  modifiche non salvate. Al salvataggio l'host ricarica la lista. Disponibile in Script, Funzioni, Test Suite.
- **Task — opzioni extra** (`NewTaskControl`):
  - **Cadenza EVENT**: trigger su evento del log Windows via `schtasks /SC ONEVENT /EC <canale> /MO "<XPath>"`.
    Il pannello EVENT ha canale (default `System`) + filtro XPath (a mano o dalla Guida).
  - **☑ Genera log** (`TaskLogging`): crea `log\<nomeTask>.log` accanto al task. **sqlcmd** → aggiunge
    `-o "log"` (args semplici, editabili). **Altri programmi** → azione **riscritta dopo la creazione** in
    `cmd.exe /c ""prog" args > "log" 2>&1"` via PowerShell `Set-ScheduledTask` (**GOTCHA quoting**, §10).
  - **❓ Guida** (`GuideWindow`, SOLO spiegazione modale): snippet copiabili (riavvio `shutdown /r /f /t 0`,
    stop `/s /f /t 0`, esempio evento 7036) + spiegazione passo-passo di come usare il generatore.
  - **🧩 Genera evento** (`EventGenWindow`): generatore semplice. Passo 1 = **cerca/scegli il servizio da una
    lista** (`Get-Service`, es. `ps_game`); Passo 2 = **quando** (qualsiasi cambio / avvio / arresto). Il filtro
    XPath (evento 7036 implicito, `param1`=DisplayName) si aggiorna live; "✓ Usa nel task" imposta la cadenza
    EVENT. Lo stato avvio/arresto usa il `param2` REALE del server (rilevato via `ScanStateTexts`: sul server
    è "running"/"stopped"; il classificatore per-keyword gestisce anche IT "in esecuzione"/"arrestato").
    NB: le **ComboBox editabili del tema sono rotte** (template senza `PART_EditableTextBox`) → picker con
    TextBox di ricerca + `ListBox`, non combo editabili.
- **Pianificati**: `schtasks /Query /FO CSV /V` (TUTTI i task) poi **filtro** su prefisso `\ShaiyaServerHub\`.
  GOTCHA: NON usare `/TN "\ShaiyaServerHub\"` (interrogare una cartella dà `ERROR: syntax incorrect` →
  lista vuota anche se i task esistono). Non uscire su rc≠0 (schtasks può darlo per task di sistema rotti).
  Abilita/Disabilita/Rimuovi via `/Change /ENABLE|/DISABLE` e `/Delete`.
- **Servizi**: `ServiceController` (servizi Windows VERI, non processi). Start=`Start-Service`,
  Stop=`Stop-Service`, **Avvia servizi**=`StartAll` (ordine sotto). Auto-refresh timer 3s mentre la
  vista è aperta + Aggiorna manuale. Stato: verde ONLINE / rosso offline / grigio non installato.
- **⚙ Impostazioni**: SQL server, auth (Windows -E / SQL login+password DPAPI), database, cartella Bin,
  bottone canale comandi. Test Connessione mostra esito colorato (verde/rosso) + log.

---

## 5. Servizi Windows gestiti (da AutoAvvioServiziShaiya.ps1)

Nomi servizi reali. Ordine di `StartAll`:
```
MSSQLSERVER → PSM_AgentServer → PSM_AgentClient →
ps_session → ps_userLog → ps_login → ps_gameLog → ps_dbAgent → ps_game
```
`PSM_AgentServer`/`PSM_AgentClient` sono i prerequisiti (lanciano i figli). SQL = `MSSQLSERVER`.
Modifica la lista in `Core\ServiceMonitor.cs` (`Watched` + `StartOrder`) se cambiano i nomi.

---

## 6. Canale comandi PSM_Cmd — nota critica

Gli script che usano `usp_RunCommand` (**4.AUTO-Boss** `/mmake`, **3.Auto-EXP-Notice** `/exp2xenable`)
vengono **negati (-3)** se il login SQL non è in `PSM_Cmd.dbo.GmCallerProfile`.
In **Windows auth (-E)** il login è sysadmin → NON è in profilo → DENIED.

Fix in-app: ⚙ Impostazioni → **"Abilita questo login nel canale comandi (ECONOMY)"**
(`CommandChannel.RegisterCurrentLoginAsEconomy` = MERGE `SUSER_SNAME()` in `GmCallerProfile` rank 3).
Alternativa: auth **SQL login = ShaiyaTaskAgent** (tier ECONOMY già in profilo).
Contesto completo del canale: vedi memoria `project_psmagent_command_channel` e la guida
`4.Installare prima questo\1.0.PSM_Cmd-SecureCommandChannel`.

`/nt` → `usp_SendNotice` (cap 150 char, sanifica). `/exp2xenable`,`/mmake` → `usp_RunCommand`
(allowlist+tier+audit `GmCommandLog`). `/mmake` è comando CUSTOM sdev.dll (allowlist Enabled=1).

---

## 7. Build & deploy + Script di default

```
dotnet build ShaiyaServerHub.sln -c Release
# → ShaiyaServerHub\bin\Release\ShaiyaServerHub\  (exe + scripts\ functions\ testsuite\ già popolati)
```

**Script di default (versionati):** `ShaiyaServerHub\DefaultScripts\` con struttura
`scripts\/functions\/testsuite\` (sql\+ps1\). Sono la sorgente-di-verità dei default.
- Nel repo i segreti sono **placeholder** (`<<CAMBIA_PASSWORD_...>>`) → niente password su GitHub.
- Password reali in `ShaiyaServerHub\.env` (in `.gitignore`).
- **Due script** (path relativi a $PSScriptRoot → repo spostabile):
  - `ShaiyaServerHub\Build-And-Copy.ps1` = fa TUTTO: `dotnet build` (Release/Debug) → copia
    DefaultScripts in `bin\<Config>\ShaiyaServerHub\` → **inietta** password dal `.env` → copia il `.env`.
    (Cartella output rinominata da `net48` a `ShaiyaServerHub` via csproj AppendTargetFrameworkToOutputPath=false + OutputPath.)
  - `build-and-copy.bat` (root repo) = lancia il ps1 (doppio click; arg opz. Debug).
- **Niente più target MSBuild** (rimosso CopyDefaultScripts): la copia la fa solo il ps1.
  Un plain `dotnet build` NON copia più i default → usare Build-And-Copy.ps1 / il .bat.

**Deploy sul server:** copiare l'INTERA cartella `bin\Release\ShaiyaServerHub\` (exe + cartelle script funzionanti).
Le cartelle script stanno in `bin\` (rigenerate ad ogni build dai DefaultScripts). Al primo avvio
l'app chiede UAC (admin); `hub.settings.xml`/`hub.log` si creano accanto all'exe.

**Sicurezza:** mai committare `.env`, `hub.settings.xml`, o script con password reali. I default in
`DefaultScripts\` DEVONO restare con placeholder. Vedi [[feedback_security_production]].

---

## 8. Stato attuale (fatto)

- [x] Scaffold WPF net48 + tema + IT/EN + icona + manifest admin.
- [x] Discovery script `sql\`/`ps1\` + Esegui live (sqlcmd/powershell/cmd).
- [x] Vista Script a 2 sezioni + badge colore per tipo.
- [x] Crea/Abilita/Disabilita/Rimuovi task (schtasks) + azione headless `--run`.
- [x] Servizi via ServiceController: Query + Start/Stop singolo + StartAll + auto-refresh 3s + colori stato.
- [x] ⚙ Impostazioni: SQL auth (Windows/SQL login DPAPI), database, Bin, Test con esito colorato,
      bottone registra login nel canale comandi.
- [x] Nav laterale a viste separate.
- [x] **Editor file integrato** (`EditorWindow`): ✎ Modifica per riga + ＋ Nuovo file, salva/Ctrl+S,
      preserva codifica. In Script, Funzioni, Test Suite.
- [x] **Task: log opzionale** (`TaskLogging`): sqlcmd `-o` / wrapper cmd redirect → `log\<nome>.log`.
- [x] **Task: cadenza EVENT** + **Guida** (`GuideWindow`): generatore XPath 7036 + scan servizi reali
      (verificato sul server: 500 eventi SCM 7036, `param1`="Shaiya Game Server").
- [x] **Più trigger per task** (opzionali): trigger principale + lista extra (`CadencePicker` + `TriggerDialog`).
      1° trigger via schtasks, extra appesi via PowerShell (time = New-ScheduledTaskTrigger, evento = CIM
      MSFT_TaskEventTrigger). Verificato: DAILY+WEEKLY+EVENT coesistono nell'XML. Vale anche in Edit
      (TaskXml parsa TUTTI i trigger).
- [x] **Trigger MONTHLY** (`ScheduleByMonth`: giorni-del-mese + mesi selezionati, come la `.xml` esportata
      dal Task Scheduler). UI: giorni CSV + 12 checkbox mesi in `CadencePicker`. Creazione: se un qualsiasi
      trigger è mensile, l'INTERO task (azione + tutti i trigger) si crea via **COM Schedule.Service.RegisterTask
      da XML completo** (`CreateViaXml`), NON schtasks+CIM — il layer CIM non sa ri-serializzare un mensile.
      Edit ok (`TaskXml` parsa `ScheduleByMonth`). Verificato sul server: giorno 21 · Gen/Apr/Lug/Ott, run OK.
- [x] **Guardia anti-zombie**: l'exe del Hub senza `--run` è rifiutato in creazione/edit e mai avvolto in
      `cmd > log` (vedi §10 "cmd-redirect wrapper appende la UI e locka il log").
- [x] **Modulo Web server** (`Core/WebServer/`, nav "🌐 Web server"): astrazione `IWebServer`
      (`XamppApache` attivo + `Iis` stub), `ServiceMonitor.Query(services[])` per servizi web separati
      dai `ps_*` (solo `Apache2.4`), `WebProbe` (health `status.php`, `mod_status ?auto`, scadenza cert TLS).
      Start/Stop/Riavvio, rigenera SSL, backup web, apri webroot/log.
- [x] **Editor Settaggi sito** (`WebSettingsWindow`, bottone "⚙ Settaggi sito" nel modulo Web server):
      legge/scrive `PS_WebSite.dbo.Web_Settings` via `Ernoweb@` (`Core/WebServer/WebSettingsRepo.cs`).
      Raggruppato per categoria, validazione JSON `*_Permissions`, righe read-only heartbeat/secret.
- [x] **Dashboard** (`PanelDashboard`, nav "🏠 Dashboard", **vista di default**): panoramica con 4 tessere
      KPI cliccabili (Servizi/Task/Script/Web) + corpo con card Servizi di gioco (sx) e **griglia 2×2** dx
      (Web health+cert · Firewall · PSM_Cmd · Test Suite) + Azioni rapide, tutte navigabili (`SelectView`).
      La tessera Task mostra pallini ●OK/●errore; la card **Test Suite** legge `testsuite\testsuite.log`
      (`Core/TestSuiteReport.cs`) e mostra ●PASS/●WARN/●FAIL dell'ultimo run. Dati veloci sul timer 3s;
      sonde pesanti off-thread con throttle TTL 20s → apertura istantanea, nessun re-probe ad ogni switch di
      vista. Localizzata IT/EN (chiavi `Dash*`/`NavDashboard` in it.xaml **e** en.xaml + testi dinamici in `L.cs`).

> **NB — pivot 2026-07-05:** la vista "Web jobs" (coda DB `Web_TaskJobs`) è stata **RIMOSSA** perché
> ridondante con Script + Pianificati. I job del sito sono normali script pianificati (metti in `scripts\`,
> usa ▶ Pianifica). Il worker.ps1/`ShaiyaSchedulerWorker` è eliminato dal server (era test).

---

## 8b. Web server + Settaggi sito — dettagli

**Due connessioni SQL** (memoria `sql-accounts-split`):
- **game** = impostazioni SQL correnti (`ShaiyaTaskAgent`, solo PSM_Cmd) → `usp_RunCommand` (ECONOMY).
- **web** = `Ernoweb@` via variabili ambiente `DB_USER`/`DB_PASSWORD` (come il sito) → SOLO l'editor
  Settaggi sito (`Web_Settings`). Configurabile in ⚙ Impostazioni → "CONNESSIONE SITO" (Env default / Windows / SQL login).

**Gotcha risolti (Web):**
- `Invoke-WebRequest` HTTPS self-signed: NON usare scriptblock come `ServerCertificateValidationCallback`
  (thread threadpool senza runspace → "There is no Runspace available"). Usare tipo **compilato**
  `ICertificatePolicy` via `Add-Type`. Il cert probe SslStream è sincrono → scriptblock ok lì.
- Parsing output sqlcmd (`-W -s "|"`): SELECT a **colonna singola senza nome** → header vuoto → NON contare
  le righe. `WebSettingsRepo.Rows()` individua la riga separatore `---` e prende il resto. Valori settings
  letti in HEX (UTF-16LE) per gestire `|`/newline.

---

## 9. TODO / prossime implementazioni

0. **Pagina "Task" del sito** — invariata; in futuro forse sola-lettura dei task pianificati (da decidere).
1. **Wizard "nuovo server"** — procedura guidata che esegue in ordine gli step di
   `A-Best-Installation-GUIDE` (SQL setup, PSM_Cmd install 00→07, servizi, task). Il modulo più grande.
2. **Import script dalla cartella NAS** — bottone in Settings per copiare gli script aggiornati da
   `5.Script-Server AUTO` dentro `sql\`/`ps1\` con un click (evita copia manuale post-rebuild).
   Meglio ancora: cartella master `dist-scripts\` fuori da `bin\` + copia in output al build.
2b. **Persistenza script fuori da bin** — spostare `sql\`/`ps1\` in un path stabile (es. `%ProgramData%\ShaiyaServerHub`)
   così sopravvivono ai rebuild; oppure percorso configurabile in Settings.
3. **Editor cadenza avanzato** — "esegui anche se mancato" (`/Z`, condizioni), preview del comando
   schtasks generato. (WEEKLY multi-giorno, **MONTHLY** giorni+mesi, LOGON/BOOT/SHUTDOWN/**EVENT** già fatti.)
4. **Toggle ON/OFF preset** — set predefinito di job noti (Auto-EXP, Auto-Boss, Backup DB 04:00,
   pulizia log, watchdog ps_game) con un interruttore che crea/abilita il task giusto già configurato.
5. **Log persistente + viewer** — filtro per livello, apertura `hub.log`, export.
6. **Dashboard stato** — riquadro riassuntivo (servizi up/down, ultimi run task, spazio disco backup).
7. **Audit canale comandi** — vista che legge `PSM_Cmd.dbo.GmCommandLog` (chi ha eseguito cosa).
8. **Editor: syntax highlighting** — evidenziazione SQL/PS nell'editor (ora è testo semplice). ✅ base fatta.
9. **Notifiche** — alert se un servizio va giù o un task fallisce (exit ≠ 0).

---

## 10. Gotcha / lezioni (non ripetere gli errori)

- **MC1000 build WPF**: non referenziare `System.Web.Extensions` (trascina `System.Web`). Usare XmlSerializer.
- **DragMove ruba il click**: mettere `MouseLeftButtonDown="DragWindow"` SOLO sul titolo, non sull'intero
  Grid header, altrimenti i bottoni (X, ecc.) dentro l'header non ricevono il click. Glifo icona → `IsHitTestVisible=False`.
- **ScrollBar tema**: serve stile **globale implicito** `TargetType="ScrollBar"` con template verticale +
  orizzontale (trigger su `Orientation`), altrimenti restano bianche di Windows.
- **Servizi = Windows services, non processi**: usare `ServiceController` (Start-Service/Stop-Service),
  NON lanciare/killare gli exe. I nomi servizi vengono da `AutoAvvioServiziShaiya.ps1`.
- **Nav Checked durante InitializeComponent**: i pannelli non esistono ancora → `ShowView` fa null-guard,
  poi il ctor chiama `ShowView("dashboard")` esplicito (vista di default).
- **RadioButton nav**: `GroupName="nav"` per mutua esclusione; stato attivo via trigger `IsChecked`.
- **Dizionari IT/EN allineati (OBBLIGATORIO)**: ogni chiave nuova va in `Lang/it.xaml` **E** `Lang/en.xaml`
  (mai una sola). Testi dinamici con numeri → `L.cs` con `S(it,en)`. **Verifica sempre con diff** delle
  `x:Key` prima di considerare fatto: `grep -oP 'x:Key="\K[^"]+' it.xaml|sort -u` vs stesso su en.xaml →
  `comm` deve dare zero differenze (atteso: N == N chiavi). Vedi memoria `always-localize-it-en`.
- **Dashboard lag**: NON fare `schtasks`/`Discover`/probe PowerShell sincroni sul thread UI ad ogni
  `ShowView` → freeze. Pesante in `Task.Run` + throttle TTL (i TextBlock tengono l'ultimo valore, riapertura
  istantanea). Solo `ServiceMonitor` (API in-process, cheap) resta sincrono/nel timer 3s.
- **Quoting schtasks / PowerShell (CRITICO)**: `ProcessStartInfo.Arguments` è UNA stringa ri-parsata da
  `CreateProcess` → le **virgolette annidate vengono strippate**. TESTATO: `schtasks /TR "…"` con wrapper
  `cmd /c ""prog" args > "log" 2>&1"` si rompe (schtasks vede `>` come argomento). Idem `powershell -Command "…"`
  con doppie virgolette interne. **Soluzione**: passare lo script PowerShell via **`-EncodedCommand`** (Base64
  UTF-16LE) → `ProcessRunner.RunPowerShellEncoded`. Per il log-wrapper si crea prima il task (azione semplice),
  poi si **riscrive l'azione** con `New-ScheduledTaskAction`+`Set-ScheduledTask` via EncodedCommand (verificato:
  Arguments memorizzati con tutte le virgolette intatte).
- **Evento 7036**: usato da **più provider** (anche driver, es. WiFi `Netwtw08`) con schema `EventData` diverso.
  SOLO il provider **`Service Control Manager`** ha `<Data Name='param1'>` (nome servizio) e `param2` (stato,
  **LOCALIZZATO**: IT "in esecuzione"/"arrestato"). Lo scan filtra `ProviderName='Service Control Manager'`.
  Il filtro EVENT di default matcha solo `param1` (nome servizio) per evitare la trappola del testo localizzato.
- **Verifica sul server vs dev PC**: l'app gira SUL server (RDP), non sul PC di sviluppo. Test di *quoting*
  (schtasks/PS) sono machine-independent; test che dipendono dal *contenuto* eventi (scan 7036) vanno verificati
  sul server reale via SSH (`administrator@10.10.5.91`) — il dev PC può non avere eventi SCM 7036.
- **ComboBox editabile ROTTA nel tema**: il template custom in `AppStyles.xaml` NON ha `PART_EditableTextBox`
  → `IsEditable="True"` non mostra/aggiorna testo e `TextChanged` non scatta (preview "morta"). Per input
  con ricerca usare **TextBox + ListBox** (vedi EventGenWindow), non combo editabili. Le combo NON editabili
  (SelectionChanged/SelectedItem) funzionano.
- **`Path.GetFileName*` esplode su virgolette**: `Illegal characters in path` se il Program di un task esistente
  contiene `"` (task vecchi in Edit). NON usare `Path.*` su stringhe non fidate: `TaskLogging.IsSqlcmd` fa
  parsing manuale (LastIndexOfAny `\`/`/`) + `StripQuotes`.
- **`$ProgressPreference`**: PowerShell con stderr redirect emette lo stream *progress* come `#< CLIXML` su
  stderr → catturato come output. `RunPowerShellEncoded` prepende `$ProgressPreference='SilentlyContinue'`.
- **Delay trigger evento**: la `.xml` vecchia aveva `<Delay>PT1M</Delay>` (attende dopo l'evento — dà tempo a
  server/SQL di partire). Nostro supporto: `ScheduleSpec.EventDelayMinutes`; primary via schtasks `/DELAY mmmm:ss`
  (0001:00 = 1 min), extra via CIM `$e.Delay='PT1M'`. TaskXml parsa `<Delay>` (PT_H/_M/_S → minuti). Verificato.
- **Più trigger per task**: `schtasks /Create` fa UN solo trigger. Gli extra si appendono via PowerShell
  `Set-ScheduledTask` (`$t.Triggers += ...`): a tempo con `New-ScheduledTaskTrigger`, su evento con CIM
  `New-CimInstance MSFT_TaskEventTrigger` (namespace `Root/Microsoft/Windows/TaskScheduler`, imposta
  `.Enabled` + `.Subscription=<QueryList>`). Passare il tutto via `-EncodedCommand`. WEEKLY `-DaysOfWeek`
  vuole i nomi inglesi interi (Monday…). In Edit il task si RICREA (`/F`) con tutti i trigger (TaskXml li parsa tutti).
- **param2 evento 7036 = testo di stato**: sul server reale è **"running"/"stopped"** (OS EN), ma è comunque
  LOCALIZZATO in generale. Il generatore lo rileva dagli eventi e classifica per keyword (`run`/`esec`, `stop`/`arrest`);
  il default "qualsiasi cambio" (nessun `param2`) evita del tutto la trappola.
- **Trigger MONTHLY: CIM NON lo sa ri-serializzare (CRITICO)**: `schtasks /SC MONTHLY` crea il
  `CalendarTrigger/ScheduleByMonth`, MA `New-ScheduledTaskTrigger` **non ha `-Monthly`** e soprattutto
  `Get-ScheduledTask | Set-ScheduledTask` (CIM) su un task che HA già un trigger mensile **fallisce**
  (`Parametro non corretto`: il mensile torna come `MSFT_TaskTrigger` generico non round-trippabile).
  Conseguenza subdola: `SetAction`/`AddExtraTriggers` (che passano da CIM) falliscono in silenzio → l'azione
  resta con soli `Command`, **Arguments/WorkingDirectory/--log spariscono**. Soluzione: se un qualsiasi
  trigger è mensile, costruire l'**intero task via COM** `Schedule.Service.RegisterTask` da **XML completo**
  (`TaskSchedulerService.CreateViaXml` + `BuildTaskXml`/`BuildTriggerXml`, formato identico alla `.xml` del
  Task Scheduler). `RegisterTask(path, xml, 6=CREATE_OR_UPDATE, 'SYSTEM', $null, 5=SERVICE_ACCOUNT)`.
  Verificato: round-trippa DaysOfMonth/Months, Arguments e WorkingDirectory intatti.
- **cmd-redirect wrapper appende la UI e LOCKA il log (CRITICO)**: un'azione `cmd /c ""ShaiyaServerHub.exe" >
  "log" 2>&1"` **senza `--run`** è doppiamente rotta: (1) l'exe del Hub senza `--run` parte in **modalità UI**
  (WinExe headless) e **non termina mai** → il `cmd` genitore resta vivo per sempre; (2) il redirect `> log`
  tiene il file **lockato in scrittura esclusiva** → ogni run successivo fa `AppendAllText` → *sharing violation*
  → catch silenzioso → **log a 0 byte** (sintomo: "Genera log" sembra non funzionare). Diagnosi sul server:
  `Get-Process ShaiyaServerHub | ? SessionId -eq 0` (zombie in session 0, parent = svchost `netsvcs` = Task
  Scheduler) e `Win32_Process.CommandLine`; il file lockato non è nemmeno cancellabile. **Guardia** (3 livelli):
  `TaskLogging.IsHubExe(program)`; in `NewTaskControl` create/edit si RIFIUTA l'exe del Hub senza `--run`
  (`L.HubExeNeedsRun`); in `Create` (schtasks) e `BuildTaskXml` (COM) l'exe del Hub non è **mai** avvolto in
  `cmd > log` (i task hub loggano via `--log`, non col redirect). Se ricapita: killa lo zombie in session 0.

---

## 11. Repository GitHub

- Repo: **https://github.com/K3rn3l-P/ShaiyaServerHub** — branch `main`.
- Git user: `K3rn3l-P`. Push via HTTPS (credential manager). `gh` CLI non su PATH bash → usare `git` diretto.

### Cosa NON committare mai (già in `.gitignore`)
| Escluso | Perché |
|---|---|
| `bin/` `obj/` `[Bb]in/` `[Oo]bj/` | Build output |
| **`sql/` `ps1/`** | Script utente (vecchi/nuovi) — vivono solo sul server, aggiunti a runtime. **Non pushare** |
| **`hub.settings.xml`** | Contiene la password SQL cifrata DPAPI → mai nel repo |
| `hub.log` | Log runtime |
| `.vs/` `*.user` `*.suo` | Roba Visual Studio locale |

> **Regola:** gli script `.sql`/`.ps1` NON vanno nel repo. Sono doppiamente ignorati (pattern `sql/`+`ps1/`
> e stanno dentro `bin/`). Verifica prima di ogni push: `git ls-files | grep -E '\.sql$|\.ps1$'` deve essere vuoto.

### Workflow push
```
git add -A
git commit -m "..."
git push               # main tracciato su origin/main
```
Verifica pulizia staged: `git status --short` non deve mostrare file sotto `sql/`, `ps1/`, `bin/`, né `hub.settings.xml`.

---

## 12. Riferimenti

- Tema/pattern UI sorgente: `C:\Users\lol\source\repo\shaiya-legacy-updater\PatchMaker`.
- Script AUTO + canale comandi: `\\10.10.19.2\...\A-Best-Installation-GUIDE\5.Script-Server AUTO`
  e `4.Installare prima questo\1.0.PSM_Cmd-SecureCommandChannel`.
- Memorie Claude collegate: `project_shaiya_server_hub`, `project_psmagent_command_channel`,
  `project_command_manager_port` (comando /mmake custom).
