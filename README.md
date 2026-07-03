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
2. **Nuovo task** — seleziona script + cadenza (DAILY/HOURLY/MINUTE/WEEKLY/ONCE) → crea uno
   Scheduled Task in `\ShaiyaServerHub\`. L'azione è `ShaiyaServerHub.exe --run "<script>"`
   (headless, auth/log centralizzati, nessuna password in chiaro nel task, gira come SYSTEM).
3. **Task pianificati** — lista dei task `\ShaiyaServerHub\`: Abilita / Disabilita / Rimuovi.
4. **Stato servizi** — presenza processi `ps_game`, `ps_login`, `PSM_Agent`, `sqlservr`, … con
   Stop e Avvia (via script di avvio configurato).
5. **⚙ Impostazioni** — server SQL, auth (Windows `-E` / SQL login), database, percorsi, e il
   bottone **canale comandi** (vedi sotto).

## ⚠️ Canale comandi PSM_Cmd (importante)
Gli script che usano `usp_RunCommand` (Auto-Boss `/mmake`, Auto-EXP `/exp2xenable`) sono negati
(-3) se il login SQL non è in `PSM_Cmd.dbo.GmCallerProfile`. In **Windows auth (-E)** il login è
sysadmin → NON è in profilo → DENIED.

Fix nell'app: ⚙ Impostazioni → **"Abilita questo login nel canale comandi (ECONOMY)"** registra
il login corrente con tier ECONOMY. In alternativa usa auth **SQL login = ShaiyaTaskAgent**.

## Build
```
dotnet build ShaiyaServerHub.sln -c Release
```
Output: `ShaiyaServerHub\bin\Release\net48\ShaiyaServerHub.exe`. Copiare l'exe sul server; le
cartelle `sql\`/`ps1\` si creano al primo avvio, poi popolarle con gli script.

## TODO (prossimo)
- Wizard "nuovo server" (esegue in ordine gli step di `A-Best-Installation-GUIDE`).
- Editor cadenza avanzato + import automatico degli script dalla cartella NAS.
