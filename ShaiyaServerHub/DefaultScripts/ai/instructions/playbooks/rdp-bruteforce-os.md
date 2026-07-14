# Playbook — Brute force RDP/SSH a livello Windows (OS, non gioco)

## Segnale
`windows_event_log(focus:"logons")` mostra molti login falliti (Security 4625) o un account lockout
(4740) dallo stesso IP, con `LogonType` 10 (RemoteInteractive = RDP) o 3 (rete/SMB/SSH). **Diverso** da
`bruteforce-login.md`: quello è il login DI GIOCO (tabella `LoginAttempts`), questo è il login WINDOWS
del server stesso — molto più critico, un successo qui è compromissione totale del server, non di un
account giocatore.

**Importante**: `windows_event_log(focus:"logons")` filtra GIÀ il rumore interno noto (fallimenti
Kerberos di `MSSQLSERVER` senza IP, verificato sul server reale — decine di eventi normali che NON sono
tentativi esterni). Se il tool ritorna "nessun evento con IP reale", il rumore di fondo è già escluso,
non serve indagare oltre.

## Diagnosi (corroborazione multi-sensore)
1. `windows_event_log(focus:"logons")` — quanti tentativi, da quale IP, che tipo di logon.
2. `network_port_audit`/`firewall_status` — la porta RDP (3389) o SSH (22) è davvero esposta pubblicamente,
   o solo su LAN? Un attacco su una porta chiusa da fuori non dovrebbe nemmeno arrivare.
3. `windows_event_log(focus:"restarts")` — escludere che il riavvio del server stesso spieghi un picco
   di eventi (es. servizi che ripartono generano falsi "fallimenti" nella finestra del boot).

## Mitigazione — SOLO con i tool che esistono oggi
- **Non esiste ancora un tool di blocco a livello OS/Windows Firewall per IP arbitrari.**
  `temp_block_ip` è pensato per le porte di GIOCO (30800/30810), NON assumere che copra o sia adatto a
  bloccare un attacco su 3389/22 — dillo esplicitamente se proposto.
- `kick_player`/`ban_player` sono INUTILI qui: l'attaccante non ha mai autenticato un account di gioco,
  sta tentando di autenticarsi come utente WINDOWS del server.
- **Azione**: segnalare chiaramente all'admin (IP, conteggio tentativi, porta bersaglio) e suggerire
  hardening manuale (regola firewall specifica sull'IP, RDP dietro VPN/whitelist, verificare NLA
  abilitato — vedi `19_rdp_hardening.ps1` già nella Test Suite). NON tentare azioni automatiche su
  SSH/RDP: sono l'accesso dell'amministratore, intoccabili per costruzione (vedi `rules.md`).

## NON fare
- Non toccare MAI porte/regole SSH (22) o RDP (3389) — invariante assoluto del progetto.
- Non dichiarare "sotto attacco" per un singolo fallimento isolato — serve un conteggio concreto da un
  IP specifico (le soglie sono già configurate nel digest automatico, `AiDigestOsFailedLogonMedium`/`High`).
