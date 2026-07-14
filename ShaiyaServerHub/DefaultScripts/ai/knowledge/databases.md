# Database

## Elenco
`PS_Billing` · `PS_ChatLog` · `PS_GameData` · `PS_GameDefs` · `PS_GameLog` · `PS_UserData` ·
`PS_WebSite` · `PSM_Cmd`.

## Mapping account applicativi (ognuno legato al SUO database, least-privilege)
| Servizio | Database | Nota |
|---|---|---|
| ps_dbAgent | PS_GameData | |
| ps_game | PS_GameDefs + PS_GameData | |
| ps_gameLog | PS_GameLog | |
| ps_gameLog | PS_ChatLog | |
| ps_login | PS_UserData | |
| ps_userLog | PS_UserData | |
| sito web (Ernoweb@) | PS_WebSite | tier ECONOMY nel canale comandi |
| worker/task (ShaiyaTaskAgent) | PSM_Cmd | tier ECONOMY, unico login con accesso al canale comandi da script |

I nomi di login reali sono casuali (es. `Scr%1@be1422`) per componente — vedi `accounts.md` per la
lista completa (NEVER-TOUCH), le password NON sono mai da leggere/loggare da parte dell'AI.

## Tabelle di sicurezza rilevanti (telemetria nativa — i "sensori d'attacco" del server)
Per queste 3 tabelle **preferisci sempre `security_telemetry`** (query fisse già verificate, corrette
per costruzione) a `sql_read_query` fatto a mano — se comunque ti serve una query diretta, ECCO lo
schema REALE (verificato via SSH, non ipotesi — nomi colonna diversi da quello che ti aspetteresti):
- `PS_UserData.dbo.SecurityEvents`: eventi tamper/speedhack/rangehack/dll-injection scritti da sdev.dll.
  Colonne: `Id, EventTime, Username, IP, EventType, Detail`. **NON** `UserID`/`ClientIP`/`Timestamp`.
- `PS_UserData.dbo.LoginAttempts` + stored proc `CheckLoginRateLimit`/`sp_LoginSuccessCheck`: brute force login.
  Colonne: `AttemptID, UserID, IPAddress, AttemptTime, Success, FailureReason, UserAgent`. **NON** `IP`
  (è `IPAddress`), **NON** `LoginTime` (è `AttemptTime`).
- `PS_UserData.dbo.GameSecuritySessions`: sessioni/token di sicurezza attivi.
  Colonne: `TokenHash, UserUID, UserID, ClientIP, CreatedAt, ExpiresAt`. **NON** `CharID`/`LoginTime`/
  `LastActivity` (non esistono in questa tabella — il CharID va cercato in `PS_GameData.dbo.Chars`).
- `PS_UserData.dbo.BlockedHWID`: HWID bannati (controllato al login).
- `PS_UserData.dbo.sp_BanUser`: ban account (azione IRREVERSIBILE — mai automatica senza approvazione).
- `PS_GameData.dbo.usp_Create_BanChar_E`: ban personaggio.
- `PSM_Cmd.dbo.GmCommandLog`: log di OGNI comando GM eseguito via canale comandi (chi, quando, esito).

## Ruoli DB custom (dalla guida install MSSQL)
`MioRuoloRead` / `MioRuoloInsert` / `MioRuoloUpdate` / `MioRuoloDelete` / `MioRuoloExecute` — principio
del privilegio minimo, ogni login ha SOLO i ruoli che gli servono nel SUO database.

## Login AI dedicato
`ShaiyaAiReader` — SOLO lettura (`db_datareader`/`MioRuoloRead` + `VIEW SERVER STATE` + `VIEW ANY
DEFINITION`), NESSUNA scrittura. È l'UNICA identità SQL che l'AI usa per interrogare i database (tool
`sql_read_query`), mai le credenziali del Hub. Vedi script di creazione nella guida install MSSQL.
