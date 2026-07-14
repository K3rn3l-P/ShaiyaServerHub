# Account SQL noti (NEVER-TOUCH)

Questi login SQL fanno funzionare il server: NON vanno MAI disabilitati, alterati o droppati da
un'azione automatica, qualunque sia il sospetto. Se un'analisi suggerisce che uno di questi account sia
compromesso, è un'emergenza da segnalare all'amministratore con priorità URGENTE, MAI da risolvere
disabilitando l'account da soli.

## Login applicativi (nomi casuali per componente, least-privilege)
- Login legato a PS_GameData (ps_dbAgent/ps_game)
- Login legato a PS_GameDefs (ps_game)
- Login legato a PS_GameLog (ps_gameLog)
- Login legato a PS_ChatLog (ps_gameLog)
- Login legato a PS_UserData — usato da ps_login
- Login legato a PS_UserData — usato da ps_userLog
- `Ernoweb@` — sito web, accesso a PS_WebSite, tier ECONOMY nel canale comandi
- `ShaiyaTaskAgent` — worker/task pianificati, accesso a PSM_Cmd, tier ECONOMY
- `SecEventWriter` — usato da sdev.dll per scrivere SecurityEvents (INSERT-only, permessi minimi mirati)
- `WeaponSkinRW` — funzione dedicata (skin armi)
- `Disadu@` — solo lettura in diversi DB
- `PSMCmdOwner` — owner del DB PSM_Cmd, login DISABILITATO di proposito (non deve mai essere riabilitato
  senza motivo esplicito)

## Amministrazione
- L'account `sa` di SQL Server è stato **rinominato** (non si chiama più `sa`) per ridurre la superficie
  d'attacco — un login chiamato letteralmente `sa` che ricompare è un segnale d'allarme forte.
- Login Windows dell'amministratore del server: accesso pieno, INTOCCABILE.

## AI
- `ShaiyaAiReader` — login dedicato, SOLA LETTURA, creato apposta per l'AI (vedi `databases.md`).

## Regola
Un login SQL nuovo, non presente in questa lista, con permessi ampi (sysadmin, db_owner) è un forte
segnale di compromissione: da escalare subito, mai da "sistemare" disabilitandolo in autonomia senza
che l'amministratore ne sia a conoscenza (in P1 l'AI non ha comunque alcun potere di scrittura).
