# Difese esistenti sul server

## Anti-cheat lato client/gioco
- **GMSecurity** (`GMSecurity.ini`, leggibile via `read_server_file` — path
  `PSM_Client/Bin/Data/GMSecurity.ini`): anti-tamper generico — kick/ban su debugger, processi cheat
  noti, DLL injection, memory patching. Sistema HWID (ban per hardware, non solo account). Sezione
  `[IP_WHITELIST]` = IP ammessi per gli account admin (utile per correlare un accesso admin sospetto).
  **ATTENZIONE**: la sezione `[SETTINGS]` di questo file contiene le VERE chiavi crittografiche del
  protocollo client-server (TokenSecret/ClientSecret/SahKeyHex/PatchKeyHex/HwidSigKey) — `read_server_file`
  le redige AUTOMATICAMENTE (righe `[REDATTO]`), mai aggirabile. Non hai comunque motivo di aver bisogno
  del loro valore reale: se un task richiede quelle chiavi (es. key rotation), esiste già un flusso
  dedicato (`KeyRotationControl`/`Rotate-ShaiyaKeys.ps1`), non è compito tuo maneggiarle.
- **EtainShield** (`EtainShield.ini`, leggibile via `read_server_file` — path
  `PSM_Client/Bin/Data/EtainShield.ini`, nessun segreto qui, solo soglie): anti-cheat specifico gameplay:
  - `AntiSpeedHack`: velocità massima a piedi/in cavalcatura, soglie di violazione.
  - `AntiRangeHack`: distanza massima consentita per attacchi/skill.
  - `AntiCutting`: rilevazione "cutting" (skip di animazioni/azioni).
  - `AntiDupe`: protezione item bound da duplicazione.
  - Ogni modulo ha `LogOnly` (solo log, nessuna azione) come modalità più sicura — i commenti nel file
    indicano falsi positivi noti (es. lag spike), quindi un singolo evento non è prova certa di cheat.

## Difesa di rete
- Windows Firewall: default-block su tutti i profili, regole esplicite `Shaiya-*` per aprire solo le
  porte necessarie (vedi `server-facts.md`).
- Canale comandi PSM_Cmd con allowlist server-side (vedi `commands.md`): un secondo livello di gate,
  indipendente da qualsiasi client/Hub che invii comandi.

## Audit / Test Suite del Hub (`testsuite\`)
23 controlli SQL (`RESULT: PASS/WARN/FAIL`) + 12 controlli PowerShell (oggetto `{Status}`) + 2 audit web
(statico codice PHP per SQLi/XSS + probe dinamici HTTP/TLS). Coprono: superficie SQL Server,
sysadmin/login, TRUSTWORTHY/owner, utenti orfani, privilege escalation, permessi `public`, CLR
assemblies, criteri password, account `sa`, guest access, hash password Shaiya, infrastruttura
anti-cheat, account GM, backup recenti, versione/patch SQL, binding di rete SQL, credenziali in env
var, salute servizi, audit login, account servizio SQL, SQL Browser, esposizione porte, force
encryption, protocolli TLS, hardening RDP, patch OS/SMB, esposizione web.

Il tool `testsuite_status` legge l'ultimo esito aggregato; per rilanciare i controlli va usata la vista
Test Suite del Hub (l'esecuzione automatica dall'AI arriva in P3).

## Cosa manca (i "buchi" da tenere d'occhio)
- Nessuna correlazione automatica tra i segnali (SecurityEvents + LoginAttempts + porte esposte):
  oggi ognuno va letto a mano. È il compito principale dell'AI: mettere insieme i segnali.
- I falsi positivi di EtainShield non sono ancora filtrati automaticamente: un singolo evento non basta
  per concludere "attacco in corso" — cerca corroborazione da più fonti prima di dare un giudizio.
