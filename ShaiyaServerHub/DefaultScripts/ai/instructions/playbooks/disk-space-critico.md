# Playbook — Spazio disco critico

## Segnale
`disk_space` mostra un volume sotto la soglia configurata (`AiDigestDiskFreePctHigh`/`Medium` in
Impostazioni, default 5%/10% liberi).

## Diagnosi
1. `disk_space` — quale volume, quanti GB liberi realmente.
2. `read_server_file`/`read_hub_file` per capire cosa occupa spazio — candidati noti su questo server:
   log di gioco (`C:\ShaiyaServer\Logs\`, `PSM_Client\Bin\Log\`), log SQL, log del Hub stesso (`hub.log`,
   `log\`, `testsuite\testsuite.log`), backup `.bak` non ruotati.
3. `scheduled_tasks_status` — esistono già task di pulizia programmati (es. `PulisciLogServer.ps1`,
   `Pulizia-Log-database.sql`, visti nei log del Hub) che avrebbero dovuto girare? Se sono in errore o
   disabilitati, è probabilmente la causa.

## Mitigazione — SOLO con i tool che esistono oggi
- Se un task di pulizia esistente risulta in errore/mai eseguito: proponi `run_scheduled_task` (T3,
  richiede SEMPRE approvazione — il contenuto del task non è verificabile a priori, stesso principio di
  sempre).
- **Nessuna cancellazione autonoma di file in V1** — nessun tool esiste per farlo, e comunque non
  sarebbe appropriato deciderlo da soli su un server di produzione. Segnala cosa occupa spazio e lascia
  la decisione all'admin.

## NON fare
- Non proporre di cancellare file direttamente (nessun tool per farlo, e non sarebbe comunque
  un'azione da automatizzare senza supervisione su un server di produzione).
- Non ignorare un disco critico solo perché "probabilmente si libera da solo" — segnalalo sempre, è un
  problema concreto e oggettivo (a differenza di un evento isolato che richiede corroborazione).
