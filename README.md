# ShaiyaServerHub — by K3rn3l

Console locale di **automazione e controllo** del server Shaiya. Sostituisce l'esecuzione
manuale degli script in `5.Script-Server AUTO` (SQL/PS1/BAT) e la creazione a mano dei
Task Scheduler. **Uso solo locale, direttamente sul server** (per sicurezza).

Stack: **WPF, .NET Framework 4.8, C#** (SDK-style csproj). Zero dipendenze esterne — shella
verso `sqlcmd`, `powershell`, `schtasks`, `explorer`. Tema dark/gold ripreso da PatchMaker.

## Struttura runtime
All'avvio l'app crea due cartelle accanto all'exe:

```
ShaiyaServerHub.exe
  scripts\sql\      ← *.sql della vista "Script"
  scripts\ps1\      ← *.ps1 / *.bat della vista "Script"
  functions\<scheda>\sql\   ← *.sql di una scheda "Funzioni" (es. functions\psm_cmd-scc\sql, PSM_Cmd 00→07)
  functions\<scheda>\ps1\   ← ogni scheda della vista "Funzioni" ha la sua sottocartella
  testsuite\sql\    ← *.sql della "Test Suite" (audit 01→19)
  testsuite\ps1\    ← check *.ps1 (10→21)
  hub.settings.xml   (config; password SQL cifrata DPAPI LocalMachine)
  hub.log            (log persistente)   ·   hub-tasks.log (task headless)
```

Ogni area ha la sua cartella con dentro `sql\` e `ps1\`. Ogni apertura ricarica gli script → aggiungi nuovi script senza ricompilare.

## Moduli
1. **🏠 Dashboard** — **vista di default all'avvio**. Panoramica a colpo d'occhio di tutto il server, con
   **navigazione rapida** (le tessere e i bottoni portano alla vista relativa). Contiene: 4 **tessere KPI**
   cliccabili — *Servizi online* (`X/Y`, verde tutti su / oro parziale / rosso zero), *Task pianificati*
   (totale + esiti ultimo run in pallini **●verde OK / ●rosso errore**), *Script disponibili* (totale + `SQL · PS1/BAT`),
   *Web server* (`ONLINE/OFFLINE`, verde se il game server risponde) — più il corpo con la card *Servizi di gioco*
   (mini-lista dei 9 servizi con pallino stato) a sinistra e una **griglia 2×2** a destra con quattro card: *Web
   server* (tipo, health, scadenza cert TLS), *Sicurezza · Firewall* (🟢/🟠/🔴 profili attivi + nota simplewall),
   *PSM_Cmd* (canale comandi 🟢/🔴) e *Test Suite* (esiti dell'ultimo run letti dal log: pallini **●verde PASS /
   ●oro WARN / ●rosso FAIL** + `N controlli · data`). In fondo una barra **Azioni rapide** (Avvia servizi ·
   Nuovo task · Test Suite · Funzioni · Log). **Performance**: i dati veloci (servizi) si aggiornano col timer
   3s; le sonde pesanti (`schtasks`, probe web/firewall PowerShell, lettura log Test Suite) girano **off-thread**
   e con **throttle TTL 20s** — riaprire la Dashboard è istantaneo (nessun re-probe ad ogni switch di vista); ↻ Aggiorna forza il refresh.

   > **Convenzione progetto:** ogni nuova finestra/scheda/funzione aggiunta al Hub deve **valutare una card
   > "a colpo d'occhio" in Dashboard** (tessera KPI, card corpo o slot nella griglia 2×2) — Dashboard è il punto
   > di sintesi del server. Quando si implementa un modulo nuovo, proporre la sua card e concordarne la forma.
2. **Script** — lista auto-scoperta di `scripts\sql\` e `scripts\ps1\`. `▶ Esegui` lancia subito con log live.
   **✎ Modifica** apre il file nell'**editor** integrato (modale, salva con `💾`/Ctrl+S, preserva la codifica);
   **＋ Nuovo file** crea un `.sql`/`.ps1`/`.bat` e lo apre subito. (Editor disponibile anche in Funzioni e Test Suite.)
3. **Nuovo task** — form per pianificare uno script. **Più trigger per task** (opzionali): un **trigger
   principale** + una lista di **trigger aggiuntivi** (**＋ Aggiungi trigger**, ✎ modifica, ✕ rimuovi) —
   es. DAILY 10:00 **e anche** su evento. Vale sia per task nuovi che per l'Edit di task esistenti (vecchi
   inclusi: tutti i loro trigger vengono caricati). Trigger supportati per ciascuno:
   DAILY / HOURLY / MINUTE / **WEEKLY (multi-giorno)** / **MONTHLY (giorni-del-mese + mesi)** / ONCE /
   **LOGON** (al login) / **BOOT** (all'avvio) / **SHUTDOWN** (allo spegnimento, evento 1074) / **EVENT**
   (trigger su evento del log di Windows: canale + filtro XPath + **ritardo** opzionale in minuti, es. attendere
   1 min dopo che il server è su prima di eseguire). Per WEEKLY selezioni più giorni con checkbox localizzate
   (Lun…Dom); per **MONTHLY** indichi i giorni del mese (CSV, es. `21` o `1,15`) e spunti i mesi (Gen…Dic —
   vuoto = ogni mese), come la `.xml` esportata dal Task Scheduler. Sezione **Azione** (come Task Scheduler): **Programma/script**, **Argomenti**, **Avvia in**
   (prefillati con `ShaiyaServerHub.exe --run "<script>"` ma modificabili). Opzioni: **☑ Genera log**
   (crea `log\<nome>.log` accanto al task — sqlcmd via `-o`, altri programmi via wrapper `cmd` con redirect),
   **❓ Guida** (modale: spiega riavvio server `shutdown /r /f /t 0` e i trigger su evento, con snippet
   copiabili) e **🧩 Genera evento** (modale generatore: **cerchi il servizio dalla lista** — es. `ps_game` —
   scegli *quando* scattare _qualsiasi cambio / avvio / arresto_, il filtro XPath si genera da solo e "Usa
   nel task" imposta la cadenza **EVENT**). Dalla riga di uno script, **▶ Pianifica** apre lo stesso form in
   un **modale** (`NewTaskControl` condiviso).
4. **Task pianificati** — lista dei task di `\ShaiyaServerHub\`: **▶ Ora** (avvia subito, test),
   **✎ Modifica** (modale precompilato dai valori attuali del task, letti da `schtasks /Query /XML`),
   Abilita / Disabilita / Rimuovi, e **🪟 Task Scheduler** (apre la GUI di Windows per modifiche avanzate).
   Ogni operazione mostra l'esito (verde/rosso) in cima al pannello.
5. **Stato servizi** — presenza processi `ps_game`, `ps_login`, `PSM_Agent`, `sqlservr`, … con
   Stop e Avvia (via script di avvio configurato).
6. **🌐 Web server** — controllo del web server del sito (XAMPP/Apache ora, IIS predisposto via
   `IWebServer`). Stato/Start/Stop/Riavvio del servizio (`Apache2.4`), **health check** (sonda
   `Updater/api/status.php` → web/game OK, metriche `mod_status ?auto`, **scadenza certificato TLS**
   dal handshake reale su 443), rigenera SSL (`genera_chiavi_ssl-*.ps1`), backup web, apri webroot/log,
   e **⚙ Settaggi sito** → editor di `PS_WebSite.dbo.Web_Settings` (via account `Ernoweb@`): chiavi
   raggruppate per categoria (Drops · Permessi · Lucky Chest · Scheduler · Altri), validazione JSON per
   i `*_Permissions`, righe read-only per heartbeat/secret. Salva testo grezzo (il sito rilegge con `json_decode`).
7. **⚙ Impostazioni** — **connessione SQL di gioco** (server, auth Windows `-E` / SQL login, database) +
   bottone **canale comandi**; **connessione SQL del sito** separata (Env = variabili `DB_USER`/`DB_PASSWORD`
   come il sito / Windows / SQL login) + database sito, usata dall'editor Settaggi sito; config **web
   server** (tipo XAMPP/IIS, path, URL health).

> I "job del sito" (EXP notice, ecc.) sono normali script pianificati: mettili in `scripts\` e usa
> **▶ Pianifica** / la vista **Pianificati** come qualsiasi altro task. Non c'è una vista dedicata separata.

## Dove finiscono i task pianificati
I task creati dall'app vivono in una **cartella dedicata** del Task Scheduler:
`Task Scheduler Library\ShaiyaServerHub`.

- **Come trovarli**: apri *Utilità di pianificazione* (taskschd.msc) → *Libreria Utilità di pianificazione*
  → cartella **ShaiyaServerHub** (è una cartella di primo livello, sorella di `Microsoft`, **non** dentro).
- **Perché una cartella**: raggruppa i task dell'app (facili da trovare/rimuovere, non si mischiano ai tuoi);
  è anche la cartella che la vista "Pianificati" interroga.
- **Come funzionano**: identici a qualsiasi altro Scheduled Task. Girano come **NT AUTHORITY\SYSTEM**
  a **livello massimo** (`/RU SYSTEM /RL HIGHEST`) → nessun problema di permessi (anzi, il massimo).
  L'azione richiama l'exe con `--run "<script>"` (headless). Puoi eseguirli anche a mano dalla GUI
  (tasto destro → *Esegui*): fanno la stessa cosa dello scatto pianificato.
- **Nota**: girando come SYSTEM, gli script del **canale comandi** (`/mmake`, `/exp2xenable`) richiedono
  che il login `NT AUTHORITY\SYSTEM` sia in `GmCallerProfile` (vedi sezione sotto), oppure usare auth
  `ShaiyaTaskAgent`. Gli altri script (backup, notice, pulizia) non hanno questo requisito.

## ⚠️ Canale comandi PSM_Cmd (importante)
Gli script che usano `usp_RunCommand` (Auto-Boss `/mmake`, Auto-EXP `/exp2xenable`) sono negati
(-3) se il login SQL non è in `PSM_Cmd.dbo.GmCallerProfile`. In **Windows auth (-E)** il login è
sysadmin → NON è in profilo → DENIED.

Fix nell'app: ⚙ Impostazioni → **"Abilita questo login nel canale comandi (ECONOMY)"** registra
il login corrente con tier ECONOMY. In alternativa usa auth **SQL login = ShaiyaTaskAgent**.

## Due account SQL (non intercambiabili)
- **`ShaiyaTaskAgent`** = solo **`PSM_Cmd`** → `usp_RunCommand` (tier ECONOMY: `/exp2xenable`, `/mmake`).
  È la **connessione di gioco** dell'Hub (usata da tutti gli script SQL / task).
- **`Ernoweb@`** = account del **sito web**, credenziali nelle **variabili d'ambiente** `DB_USER`/`DB_PASSWORD`.
  Accede a **`PS_WebSite`**. È la **connessione del sito**, usata SOLO dall'editor **Settaggi sito**
  (`Web_Settings`). In Impostazioni si configura a parte (default = variabili ambiente).

## Script di default (DefaultScripts\ + .env)
Gli script di default vivono, versionati, in `ShaiyaServerHub\DefaultScripts\` con la stessa
struttura del runtime (`scripts\ functions\ testsuite\`, ognuna con `sql\`+`ps1\`).

- **Nel repo** i segreti sono **placeholder** (`<<CAMBIA_PASSWORD_...>>`): niente password su GitHub.
- Le **password reali** stanno in `ShaiyaServerHub\.env` (in `.gitignore`, mai committato).

## Build + copia (due script, path relativi)
Due soli script — funzionano ovunque tu sposti il repo:

| File | Dove | Cosa fa |
|---|---|---|
| **`ShaiyaServerHub\Build-And-Copy.ps1`** | dir progetto | Fa TUTTO: `dotnet build` Release/Debug → copia `DefaultScripts\` in `bin\<Config>\ShaiyaServerHub\` → **inietta le password dal `.env`** → copia il `.env`. |
| **`build-and-copy.bat`** | root repo | Doppio click → lancia il ps1. `build-and-copy.bat Debug` per Debug. |

```
# equivalente da riga di comando:
powershell -File ShaiyaServerHub\Build-And-Copy.ps1            # Release
powershell -File ShaiyaServerHub\Build-And-Copy.ps1 -Config Debug
```

Risultato: `bin\Release\ShaiyaServerHub\` (cartella rinominata dal csproj, non `net48`) = exe +
cartelle script **funzionanti** (password reali) + `.env`, pronto per copia-incolla sul server.
**Non** c'è più un target MSBuild: la copia la fa solo il ps1.

> ⚠️ Senza `.env` (es. clone pulito) gli script install PSM_Cmd restano con i placeholder:
> riempili a mano prima di eseguirli. Per aggiornare i default modifica `DefaultScripts\`
> (placeholder per i segreti) e, se cambi una password, aggiorna il `.env`.

## TODO (prossimo)
- Pagina "Task" del **sito web**: al momento invariata; in futuro forse sola-lettura dei task pianificati
  (da decidere). Il vecchio worker.ps1 / coda `Web_TaskJobs` è dismesso (era di test).
- Wizard "nuovo server" (esegue in ordine gli step di `A-Best-Installation-GUIDE`).
- Import automatico degli script dalla cartella NAS.
- Editor: syntax highlighting SQL/PS + preview del comando schtasks generato.
