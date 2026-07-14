# Playbook — Tamper/speedhack/cheat (EtainShield, `SecurityEvents`)

## Segnale
`security_telemetry(focus:"events")` mostra eventi diversi da `RANGE_HACK_PVE`/`RANGE_HACK_PVE_INFO`/
`PLAYER_ENTER_WORLD`/`PLAYER_LEAVE_WORLD` (già filtrati come rumore/telemetria normale): `TAMPER`,
`TAMPER_DETECTED`, `RANGE_HACK_PVE_VIOLATION`, `SKILLCUT_CHAIN`, `HWID_MISSING`, `WSKIN`.

## Contesto importante — EtainShield agisce già da solo lato client
`sdev.dll`/EtainShield ha una propria pipeline di enforcement (soglie `ViolationLimit`/`Kick`/`Ban`/
`LogOnly` in `EtainShield.ini`, vedi `knowledge/defenses.md`) — per i casi già gestiti (violazione grave
→ ban automatico via `sp_BanUser` con `GMID='sdev-auto'`), il lavoro è già fatto PRIMA che tu lo veda.
Il tuo ruolo qui NON è duplicare quell'enforcement, è:
1. **Correlare pattern nel tempo** — lo stesso account genera lo stesso `EventType` ripetutamente senza
   mai arrivare a un ban reale (query `security_telemetry` più volte con `minutes` diversi, o
   `sql_read_query` diretto su `SecurityEvents` filtrato per `Username`)? Potrebbe essere un falso
   positivo NOTO (es. `RANGE_HACK_PVE` ha un volume altissimo su questo server — 4494 righe osservate,
   quasi certamente soglia troppo aggressiva per un'azione precisa in-game, non un vero cheat).
2. **Segnalare pattern nuovi/anomali** — un evento MAI visto prima, o una concentrazione insolita di
   `TAMPER` in poco tempo su più account diversi (possibile attacco coordinato, non un singolo cheater).
3. **Suggerire tuning** — se identifichi un pattern di falso positivo ricorrente, proponilo esplicitamente
   all'admin (es. "l'account X genera SKILLCUT_CHAIN ogni sessione da 2 settimane senza mai un ban reale,
   valuta LogOnly per quella regola") — NON modificare tu `EtainShield.ini` (nessun tool per farlo, e
   sarebbe comunque una modifica di produzione che richiede giudizio umano).

## Mitigazione — SOLO con i tool che esistono oggi
- Se identifichi un caso con evidenza forte che EtainShield NON ha ancora gestito (es. `TAMPER_DETECTED`
  ripetuto, nessun ban seguito): puoi proporre/eseguire `kick_player` (T2) come contenimento immediato
  in attesa di revisione umana — MAI `ban_player` per un singolo evento isolato, serve corroborazione
  (più eventi, più tipi diversi, o conferma esplicita dell'admin).

## NON fare
- Non trattare `RANGE_HACK_PVE`/`RANGE_HACK_PVE_INFO` come un incidente di per sé — sono nella lista
  "rumore" filtrata di proposito da `security_telemetry(focus:"events")` per un motivo concreto
  (volume altissimo osservato su questo server specifico).
- Non proporre un ban basandoti su un solo evento — EtainShield stesso richiede una soglia di violazioni
  ripetute prima di agire, il tuo giudizio deve essere almeno altrettanto prudente.
