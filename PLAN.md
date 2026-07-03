# ShaiyaServerHub — Piano progetto / ripresa sviluppo

> Console locale di automazione e controllo del server Shaiya. **by K3rn3l**.
> Documento per riprendere il progetto e pianificare nuove implementazioni.
> Ultimo aggiornamento: 2026-07-03.

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
    MainWindow.xaml / .cs       (nav laterale + 5 viste + template riga script condiviso)
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
      ScheduledTaskInfo.cs      (modello task + ScheduleSpec)
      TaskSchedulerService.cs   (wrapper schtasks: Create/List/Enable/Disable/Delete/RunNow)
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
- **Nav laterale** (RadioButton `NavItem`) scambia 5 viste (Visibility toggle in `ShowView`):
  Script · Nuovo task · Pianificati · Servizi · Log. Auto-switch: Esegui/Start→Log, Crea task→Pianificati.
- **Vista Script**: due sezioni separate **DATABASE—SQL** (blu) e **SISTEMA—PS1/BAT** (verde),
  badge colorato per tipo (`ScriptItem.KindBrush`/`KindBadgeBg`: SQL blu, PS1 verde, BAT arancio).
- **Esegui** → `ScriptRunner`: SQL via `sqlcmd`, PS1 via `powershell -File`, BAT via `cmd /c`. Log live.
- **Nuovo task** → `TaskSchedulerService.Create`: crea task in cartella `\ShaiyaServerHub\`.
  Azione = `ShaiyaServerHub.exe --run "<scriptFullPath>"` (**headless**, App intercetta `--run` →
  `RunHeadless` → ScriptRunner, no finestra, exit code propagato). Vantaggi: auth/log centralizzati,
  **nessuna password in chiaro nel comando del task**, gira come SYSTEM (`/RU SYSTEM /RL HIGHEST`).
- **Pianificati**: `schtasks /Query /FO CSV /V` filtrato su `\ShaiyaServerHub\`; Abilita/Disabilita/Rimuovi.
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

## 7. Build & deploy

```
dotnet build ShaiyaServerHub.sln -c Release
# → ShaiyaServerHub\bin\Release\net48\ShaiyaServerHub.exe
```
**Deploy sul server:** copiare l'INTERA cartella `net48\` (exe + `sql\` + `ps1\`). Le cartelle
script stanno in `bin\` → **un rebuild/clean le cancella** → ricopiare o tenere copia master fuori da bin.
Al primo avvio l'app chiede UAC (admin). `hub.settings.xml` e `hub.log` si creano accanto all'exe.

Script già copiati (2026-07-03) da `5.Script-Server AUTO\1.Task Scheduler BACKUP`:
9 SQL in `sql\`, 11 PS1/BAT in `ps1\`.

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

---

## 9. TODO / prossime implementazioni

1. **Wizard "nuovo server"** — procedura guidata che esegue in ordine gli step di
   `A-Best-Installation-GUIDE` (SQL setup, PSM_Cmd install 00→07, servizi, task). Il modulo più grande.
2. **Import script dalla cartella NAS** — bottone in Settings per copiare gli script aggiornati da
   `5.Script-Server AUTO` dentro `sql\`/`ps1\` con un click (evita copia manuale post-rebuild).
   Meglio ancora: cartella master `dist-scripts\` fuori da `bin\` + copia in output al build.
2b. **Persistenza script fuori da bin** — spostare `sql\`/`ps1\` in un path stabile (es. `%ProgramData%\ShaiyaServerHub`)
   così sopravvivono ai rebuild; oppure percorso configurabile in Settings.
3. **Editor cadenza avanzato** — UI più ricca per WEEKLY (multi-giorno checkbox), più trigger,
   "esegui anche se mancato" (`/RU`, `/Z`, condizioni), preview del comando schtasks.
4. **Toggle ON/OFF preset** — set predefinito di job noti (Auto-EXP, Auto-Boss, Backup DB 04:00,
   pulizia log, watchdog ps_game) con un interruttore che crea/abilita il task giusto già configurato.
5. **Log persistente + viewer** — filtro per livello, apertura `hub.log`, export.
6. **Dashboard stato** — riquadro riassuntivo (servizi up/down, ultimi run task, spazio disco backup).
7. **Audit canale comandi** — vista che legge `PSM_Cmd.dbo.GmCommandLog` (chi ha eseguito cosa).
8. **Editor SQL/script integrato** — modifica rapida degli script in `sql\`/`ps1\` senza uscire.
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
  poi il ctor chiama `ShowView("scripts")` esplicito.
- **RadioButton nav**: `GroupName="nav"` per mutua esclusione; stato attivo via trigger `IsChecked`.

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
