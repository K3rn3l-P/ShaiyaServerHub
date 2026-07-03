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
  functions\sql\    ← *.sql della vista "Funzioni" (es. PSM_Cmd 00→07)
  functions\ps1\
  testsuite\sql\    ← *.sql della "Test Suite" (audit 01→19)
  testsuite\ps1\    ← check *.ps1 (10→21)
  hub.settings.xml   (config; password SQL cifrata DPAPI LocalMachine)
  hub.log            (log persistente)   ·   hub-tasks.log (task headless)
```

Ogni area ha la sua cartella con dentro `sql\` e `ps1\`. Ogni apertura ricarica gli script → aggiungi nuovi script senza ricompilare.

## Moduli
1. **Script** — lista auto-scoperta di `scripts\sql\` e `scripts\ps1\`. `▶ Esegui` lancia subito con log live.
2. **Nuovo task** — form per pianificare uno script (cadenza DAILY/HOURLY/MINUTE/WEEKLY/ONCE).
   Dalla riga di uno script, **▶ Pianifica** apre lo **stesso form in un modale** con lo script già
   preselezionato (stesso codice, `NewTaskControl`). L'azione del task è `ShaiyaServerHub.exe --run "<script>"`
   (headless, auth/log centralizzati, nessuna password in chiaro nel task).
3. **Task pianificati** — lista dei task della cartella `\ShaiyaServerHub\`: Abilita / Disabilita / Rimuovi.
4. **Stato servizi** — presenza processi `ps_game`, `ps_login`, `PSM_Agent`, `sqlservr`, … con
   Stop e Avvia (via script di avvio configurato).
5. **⚙ Impostazioni** — server SQL, auth (Windows `-E` / SQL login), database, percorsi, e il
   bottone **canale comandi** (vedi sotto).

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
- Wizard "nuovo server" (esegue in ordine gli step di `A-Best-Installation-GUIDE`).
- Editor cadenza avanzato + import automatico degli script dalla cartella NAS.
