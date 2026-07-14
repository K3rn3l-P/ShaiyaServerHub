# Playbook — Modifica di schema database rilevata (config-drift SQL)

## Segnale
`sql_audit(focus:"schema_changes")` mostra una riga CREATE/ALTER/DROP (action_id CR/AL/DR) su un
database `PS_*` — oppure il digest automatico segnala `[sql-schema] modifica schema DB: ...`. Colma lo
stesso gap che `autoruns_audit` copre per Windows, ma per il database: una modifica di schema è un
evento raro e sempre degno di attenzione, a differenza dei dati (che cambiano in continuazione durante
il gioco e NON sono auditati per volume/rumore, vedi PLAN).

## Diagnosi (corroborazione — MAI dichiarare "compromissione" da una sola riga)
1. Guarda **chi** l'ha fatto (`server_principal_name` nel dettaglio): se è l'account admin noto
   (`WIN-ACMN7B375U8\Administrator`) è quasi certamente manutenzione voluta — segnala comunque, ma senza
   allarmismo. Se è un principal SQL sconosciuto o inatteso, è un segnale molto più serio.
2. Guarda **cosa** è cambiato (`statement`): un CREATE/ALTER su un oggetto noto del progetto
   (PSM_Cmd, script di manutenzione, Test Suite) è verosimilmente legittimo; un oggetto con nome
   sconosciuto (specialmente in `PS_UserData`/`PS_GameData`) merita approfondimento.
3. Corrobora con `sql_audit(focus:"failed_logins")` e `security_telemetry` — una modifica di schema
   preceduta da tentativi di login falliti sullo stesso principal è un pattern molto più concreto di
   un singolo evento isolato.
4. Se il dubbio resta, usa `read_server_file`/`read_hub_file` per verificare se la modifica corrisponde
   a uno script di manutenzione/deploy noto in esecuzione in quel momento (task pianificato, Key
   Rotation, ecc. — `scheduled_tasks_status` per la finestra temporale).

## Mitigazione — SOLO con i tool che esistono oggi
- **Nessuna azione automatica di rollback/blocco in V1** — nessun tool esiste per annullare una modifica
  di schema, e comunque non sarebbe mai un'azione da automatizzare su un database di produzione.
  Segnala chiaramente all'admin (chi, cosa, quando) e lascia la decisione/verifica a un intervento umano.
- Se il pattern indica un account compromesso (login SQL noto con permessi ampi che ha fatto qualcosa
  di inatteso), valuta se corrisponde a un account gestibile con `ban_player`/segnalazione — ma la
  maggior parte dei login SQL con permessi di scrittura schema sono account di servizio/admin, non
  account di gioco: **quasi mai** un caso per `kick_player`/`ban_player`.

## NON fare
- Non dichiarare "il database è compromesso" da una singola modifica isolata fatta dall'admin noto.
- Non proporre di disabilitare il SQL Server Audit stesso o di modificare permessi come reazione — non
  esiste un tool per farlo ed è comunque una decisione che spetta all'admin.
- Non confondere questo con l'audit dei DATI di gioco (INSERT/UPDATE/DELETE) — quello è deliberatamente
  FUORI SCOPE (volume troppo alto su un DB di gioco live, vedi PLAN "Estensione SQL Audit").
