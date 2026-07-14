# Comandi GM in-game (canale PSM_Cmd)

Il modo REALE per far eseguire un'azione in-game (broadcast, kick, evento economia) è il canale
`PSM_Cmd`, che espone due stored procedure — usa il tool `list_game_commands` per leggere lo stato
aggiornato (Enabled/Tier) direttamente dal server invece di fidarti solo di questo file statico.

## Come funziona il gate (server-side, indipendente dal Hub)
- `PSM_Cmd.dbo.usp_SendNotice @text, @service` → invia SOLO `/nt` (broadcast), sanificato, max 150 caratteri.
- `PSM_Cmd.dbo.usp_RunCommand @service, @command` → esegue il comando SOLO se è presente in
  `GmCommandAllowlist` con `Enabled=1` E il tier del comando è ≤ tier del login chiamante
  (`GmCallerProfile`: SAFE=1, PLAYER=2, ECONOMY=3, SERVICE=4). Ritorna `-3` se negato (DENIED),
  `-2` servizio invalido, `-1` errore.
- Ogni chiamata è loggata in `PSM_Cmd.dbo.GmCommandLog` (chi, quando, comando, esito).

**Conseguenza pratica**: anche se l'AI (o un bug) proponesse `/shutdown`, il server lo rifiuta da solo
(`-3 DENIED`) perché quel comando ha `Enabled=0`. Questo è un gate reale, non solo una convenzione.

## Comandi tipicamente abilitati (SAFE — informativi, nessun impatto)
`/nt` (notice), `/si` (stato server), `/mem` (memoria), `/uc` (utenti connessi), `/servertime`,
`/viewmap`, `/chktimeout`, `/ticktime`, `/inszonecnt`, `/sl` (lista sessioni login).

## Comandi tipicamente abilitati (PLAYER — impatto su un singolo giocatore, reversibile)
`/kickuid`, `/kickcn`, `/kickcid`, `/kickun` (espellono un giocatore per UID/nome char/CharID/username),
`/nprotecton` / `/nprotectoff` (protezione anti-cheat per giocatore).

## Comandi tipicamente abilitati (ECONOMY — eventi/economia)
`/exp2xenable <rate>` / `/exp2xdisable` (moltiplicatore EXP), `/mmake <mapId mobId count x y z>` (spawn
boss/evento).

## Comandi tipicamente DISABILITATI (SERVICE — distruttivi, mai da proporre)
`/shutdown`, `/quit`, `/exit`, `/allout` (disconnette tutti), `/cstop`/`/cstart` (ferma/riavvia accettazione
connessioni — utile per "mettere in pausa" il gioco sotto attacco grave, ma richiede abilitazione
esplicita e resta un'azione ad alto impatto), `/crashdump`, `/adminopen`/`/adminclose`, `/vchkoff`.

## In P1
Nessun tool dell'AI può ancora eseguire questi comandi: `list_game_commands` serve solo a LEGGERE lo
stato dell'allowlist. L'esecuzione arriva in P2/P3 con PolicyGate ed Escalation.
