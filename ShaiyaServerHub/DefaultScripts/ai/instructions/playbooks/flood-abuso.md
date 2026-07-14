# Playbook — Flood/abuso sulle porte di gioco (30800/30810)

## Segnale
`connection_census` mostra uno o più IP con connessioni sopra soglia (`minCount`, default 8), oppure
l'admin segnala lag/lentezza sospetta durante un picco di giocatori.

## Diagnosi (SEMPRE corroborazione multi-sensore, mai un solo tool)
1. `connection_census` — quali IP hanno molte connessioni stabilite.
2. `security_telemetry(focus:"sessions")` — le sessioni di gioco attive per IP corrispondono? Un IP con
   tante connessioni TCP ma zero sessioni attive è più sospetto (handshake senza login reale) di uno con
   tante connessioni E tante sessioni (possibile NAT condiviso legittimo, es. rete aziendale/universitaria).
3. `security_telemetry(focus:"events")` — ci sono `TAMPER`/violazioni EtainShield collegate allo stesso
   account/IP nella stessa finestra?
4. Se possibile, risali dall'IP a un account specifico (via `sql_read_query` su `GameSecuritySessions`
   filtrato per `ClientIP`, colonna `UserID`) — un contenimento mirato su un account è molto meglio di
   un blocco IP indiscriminato (che potrebbe colpire giocatori legittimi dietro lo stesso NAT).

## Mitigazione — con i tool che esistono oggi
- **Account identificato con evidenza chiara** (molte connessioni + pattern anomalo + eventualmente
  eventi di sicurezza correlati): `kick_player` (T2, reversibile) — dichiara `urgency`/`confidence`
  onestamente, il sistema decide da solo se eseguire subito o mettere in coda. **Preferisci sempre
  questo a un blocco IP quando riesci a identificare l'account**: è più mirato, non rischia di colpire
  altri giocatori legittimi dietro lo stesso NAT/IP condiviso.
- **Abuso persistente dallo stesso account dopo un kick** (torna a fare la stessa cosa): valuta
  `ban_player` (T3, irreversibile, richiede SEMPRE approvazione Telegram) — motiva bene il `reason`.
- **Flood IP-level SENZA un account riconducibile** (es. tante connessioni ma nessuna sessione valida,
  quindi nessun account da kickare): `temp_block_ip(ip, reason, minutes)` (T2, reversibile — si rimuove
  DA SOLO allo scadere, mai permanente). Rifiuta per costruzione IP in LAN/Tailscale/loopback (rete di
  gestione), quindi non serve verificarlo tu prima di chiamarlo. Se emerge che era un falso positivo
  prima della scadenza naturale, usa `unblock_ip(ip)` per rimuoverlo subito.

## NON fare
- Non proporre/eseguire un contenimento basandoti su UN SOLO sensore (es. solo `connection_census` alto
  senza corroborazione).
- Non usare `temp_block_ip` come prima scelta se riesci a identificare l'account: preferisci sempre
  `kick_player`/`ban_player` (più mirati) quando possibile.
- Non inventare un tool diverso da quelli nel catalogo capacità.
