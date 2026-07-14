# Catalogo script del Hub (DefaultScripts)

Riferimento per capire cosa il server "sa già fare" tramite gli script esistenti (eseguibili dalla vista
Script/Test Suite del Hub — in P1 l'AI li legge solo tramite `testsuite_status`, non li esegue).

## functions\psm_cmd-scc\sql — installazione canale comandi PSM_Cmd
Pipeline 00→07 che crea il DB `PSM_Cmd`, la CLR assembly firmata, le tabelle `GmCommandAllowlist`/
`GmCommandLog`/`GmCallerProfile` e i due wrapper `usp_SendNotice`/`usp_RunCommand` (vedi `commands.md`).

## testsuite\sql + testsuite\ps1 — audit sicurezza (vedi `defenses.md`)
23 script SQL + 12 script PowerShell, tutti read-only, output `RESULT:`/`Status:` parsabile.

## testsuite\ps1\websecurity — pentest web applicativo
`security-audit-advanced.ps1` (scan statico codice PHP: SQLi/XSS/CSRF/session), `security-web-audit.ps1`
(probe dinamici HTTP/TLS: header sicurezza, cookie, cert, SQLi/XSS/path-traversal attivi).

## scripts\sql — utility gioco/economia (task schedulati, mutano stato)
`3.Auto-EXP-Notice.sql` (invia `/exp2xenable` in base al giorno), `4.AUTO-Boss.sql` (spawn boss casuale
via `/mmake`), `BackupdeiDatabase.sql` (backup DB, abilita/disabilita xp_cmdshell temporaneamente),
`Pulizia-Log-database.sql` (pulizia log >7gg), `Notice-Maintenance-AUTO.sql`.

## scripts\ps1 — manutenzione OS (mutano stato)
`AutoAvvioServiziShaiya.ps1` (avvio servizi in ordine), `MonitoraggioRisorse.ps1`/
`hard-MonitoraggioRisorse.ps1` (ottimizzazione RAM/CPU sotto soglia), `PulisciLogServer.ps1`,
`Re.Move-OLDBackupFiles.ps1` (rotazione backup), script generazione certificati SSL (tailscale cert).

## functions\windows-firewall\ps1 — `Apply-ShaiyaFirewall.ps1`
Applica la postura firewall standard del server (vedi `server-facts.md`).

## keyrotation\ps1 — `Rotate-ShaiyaKeys.ps1`
Rotazione chiavi crittografiche (ClientSecret/TokenSecret/PatchKeyHex), invalida le sessioni di sicurezza
attive, riavvia ps_game/ps_login. Azione pesante, mai da proporre in automatico.

## manutenzione\sql — `notice-enter.sql` / `notice-exit.sql`
Notice in-game di inizio/fine manutenzione via `usp_SendNotice`.
