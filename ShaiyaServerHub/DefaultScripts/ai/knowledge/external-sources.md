# Fonti esterne di riferimento per Shaiya (usa QUESTE, non ricerche generiche)

Quando la domanda riguarda il **protocollo di rete, lo scripting Lua, le funzioni delle librerie
server/client, o problemi di sviluppo/community tipici di un server Shaiya EP6/EP8**, preferisci una
ricerca web **mirata a queste fonti** (operatore `site:` nella query di `web_search`) invece di una
ricerca generica: una query generica come "Shaiya server vulnerabilities" restituisce spesso rumore
irrilevante (TikTok, Facebook, pagine di server pubblicitari) e spreca la ricerca. Esempio:
`site:openshaiya.org XorEncryptionSignal` invece di `Shaiya encryption packet`.

## Documentazione tecnica (fonte primaria — preferire sempre a forum/social)

- **`https://mintlify.wiki/kurtekat/shaiya-episode-6/`** — documentazione completa per lo sviluppo
  server/client Episode 6, **stessa famiglia di librerie di questo server** (conferma: `sdev.dll`,
  `sdev-db.dll`, `sdev-login.dll` sono esattamente le DLL server viste su questo server, `sdev-client`
  è la libreria lato client). Sezioni utili:
  - `reference/lua-api` — API Lua completa per AI dei mostri/comportamento (notazione ungherese:
    `dw`=DWORD, `n`=int, `b`=bool, `by`=BYTE, `sz`=stringa, `f`=float). Usa questa pagina per capire
    script Lua di mob/eventi trovati sul server (`Data\Lua\`, `Mob_AI.ini`).
  - `reference/configuration` — file di configurazione noti (utile per interpretare `.ini` in
    `Data\`: `ChaoticSquare.ini`, `SetItem.ini`, `WeaponSkins.ini`, `Obelisk.ini`, ecc. — tutti
    presenti su questo server).
  - `reference/data-formats` — formati binari (`.SData`, `.DB`) usati dal server.
  - `features/*` — sistemi di gioco (Item Mall, Chaotic Squares, Synergy, Rune Combination,
    Revenge Mark, Perfect Lapisian...) — utile per capire l'IMPATTO di un bug/exploit su un sistema
    specifico prima di proporre una mitigazione.
  - `troubleshooting` — problemi noti community per questa esatta famiglia di server.
  - Indice machine-readable: `https://mintlify.com/kurtekat/shaiya-episode-6/llms.txt`.

- **`https://openshaiya.org/docs/packets.html`** — catalogo dei pacchetti di rete incoming/outgoing
  (es. `LoginHandshakeRequest`, `XorEncryptionSignal`, `CharacterCreateRequest`, `GuildList`,
  `ItemAcquired`, `UpdateBillingPoints`...). Riferimento per capire il protocollo quando si analizza
  un sospetto attacco di manipolazione pacchetti/packet injection, o per capire cosa fa una funzione
  di `sdev.dll`/`sdev-client` vista in Ghidra.

## Community forum (per problemi/soluzioni già discussi — verificare sempre con la doc ufficiale)

- **`https://www.elitepvpers.com/forum/shaiya/`** — forum storico Shaiya private server. **Nota
  operativa**: blocca il fetch diretto di pagina (bot-detection, HTTP 403) — usa SEMPRE
  `site:elitepvpers.com <termine>` nella query di ricerca invece di provare a leggere una pagina
  specifica.
- **`https://forum.ragezone.com/community/shaiya.557/`** — forum MMO dev (sezioni: Shaiya
  Development, Shaiya Releases, Shaiya Tutorials — es. guide su decrypt di `game.exe`, crypto client).
  Utile per capire tecniche di reverse engineering/cheat già documentate pubblicamente (per sapere
  cosa un attaccante potrebbe già conoscere), o soluzioni note a bug comuni EP6/EP8.

## Repository del progetto

Repository **privati** dell'amministratore (K3rn3l-P), non raggiungibili da `web_search` (404 senza
autenticazione) — NON provare a cercarli online:

- **`Shaiya-Core`** (branch attivo: `modifiche-sync-main-v1.3s`) — DLL client (hook, overlay,
  debug panel), corrisponde a `sdev-client` nella documentazione mintlify sopra. È un **fork privato
  con modifiche proprie** basato sul progetto pubblico **`https://github.com/Spelunkern/Shaiya-Core`**
  (verificato raggiungibile — README con Repository Layout/Configuration/Feature Documentation), a sua
  volta basato sulla guida `mintlify.wiki/kurtekat/shaiya-episode-6`. **L'upstream pubblico È
  cercabile/leggibile** (`site:github.com/Spelunkern/Shaiya-Core`): utile come riferimento di struttura
  e pattern anche se la versione qui installata ha modifiche personalizzate non visibili lì. Compilato,
  gira SU QUESTO SERVER come `sdev.dll`/`sdev-db.dll`/`sdev-login.dll` in
  `C:\ShaiyaServer\PSM_Client\Bin\` — quelle DLL binarie sono il prodotto di questo repo privato, non
  leggibili come sorgente dal Hub, ma il loro comportamento a runtime è osservabile via i log in
  `Bin\Log\` e la telemetria `SecurityEvents`.
- **`shaiya-legacy-updater`** (branch attivo: `modifiche`) — sorgente dell'Updater/launcher client
  (`updater.exe`). Privato, non cercabile.

**`ShaiyaWebsite`** — il repo GitHub è privato, MA il sorgente PHP **live con tutte le modifiche
reali** è leggibile DIRETTAMENTE su questo server tramite il tool `read_web_server_file` (sotto
`C:\xampp\htdocs\`) — non serve GitHub, hai già il codice vero. Lo stesso tool legge anche la
configurazione Apache/PHP (`httpd.conf`, `php.ini`, vhosts sotto `C:\xampp\`), utile per valutare
l'hardening reale e proporre nuovi controlli Test Suite mirati. Il sito NON ha segreti hardcoded nei
file (credenziali SOLO da variabili d'ambiente — account web `Ernoweb@`), ma il tool redige comunque
in automatico righe che sembrano contenere password/secret/key/token come rete di sicurezza extra.

Se una domanda richiede codice da `Shaiya-Core`/`shaiya-legacy-updater` (client/updater, non il sito),
NON è possibile leggerlo dal Hub (repo privati, nessun accesso GitHub autenticato): dillo esplicitamente
invece di inventare contenuto, e suggerisci all'admin di consultarli lui stesso (eventualmente
confrontando con l'upstream pubblico Spelunkern/Shaiya-Core se la domanda è di struttura generale).
