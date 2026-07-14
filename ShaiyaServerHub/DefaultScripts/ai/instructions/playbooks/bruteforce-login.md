# Playbook — Brute force login

## Segnale
`security_telemetry(focus:"logins")` mostra un IP con molti `Failures` in poco tempo (`LoginAttempts.
Success=0`), oppure un singolo `UserID` con molti fallimenti seguiti da un successo (possibile account
compromesso, non necessariamente brute force esterno).

## Diagnosi
1. `security_telemetry(focus:"logins", minutes:...)` — quanti fallimenti, da quale IP, su quale finestra
   temporale. Un paio di fallimenti isolati sono normali (utente che sbaglia password); decine in pochi
   minuti dallo stesso IP su UserID diversi sono un pattern di attacco.
2. Se i fallimenti sono concentrati su UN SOLO `UserID` seguiti da un login riuscito: possibile account
   compromesso (qualcuno ha indovinato/rubato la password) — verifica `security_telemetry(focus:
   "sessions")` per capire se l'IP della sessione attiva coincide con l'IP che ha fallito prima.
3. `security_telemetry(focus:"events")` — eventi di sicurezza correlati sullo stesso account/IP.

## Mitigazione — con i tool che esistono oggi
- Un vero brute force IP-level è per natura un problema di RETE (l'attaccante prova credenziali diverse,
  non un singolo account) — `kick_player`/`ban_player` (che agiscono su un ACCOUNT già autenticato in
  gioco) non sono lo strumento giusto per fermarlo alla radice: l'attaccante non è mai riuscito ad
  autenticarsi, quindi non ha nemmeno un account/sessione da colpire.
- **Molti fallimenti concentrati da UN SOLO IP** (pattern di attacco chiaro, non un singolo account
  legittimo che sbaglia password): `temp_block_ip(ip, reason, minutes)` (T2, reversibile, si rimuove da
  solo allo scadere) — questo è il caso d'uso principale per cui esiste il tool. Il server ha comunque
  già `LoginAttempts`/`sp_LoginSuccessCheck`/rate-limit nativo lato login (vedi `knowledge/defenses.md`)
  come prima linea di difesa indipendente.
- **Account probabilmente compromesso** (fallimenti + successo, sessione attiva sospetta): segnala
  chiaramente all'admin la possibilità — un ban preventivo dell'account legittimo sarebbe controproducente
  (colpirebbe la vittima, non l'attaccante), quindi non proporre `ban_player` qui senza indicazione
  esplicita dell'admin. Un `temp_block_ip` sull'IP sospetto (se diverso da quello abituale dell'account)
  può avere senso come contenimento, ma solo se motivato chiaramente.

## NON fare
- Non bannare/kickare un account solo perché ha subito tentativi di login falliti da altri — potrebbe
  essere la vittima, non l'attaccante.
- Non dichiarare "brute force in corso" da un singolo fallimento isolato.
