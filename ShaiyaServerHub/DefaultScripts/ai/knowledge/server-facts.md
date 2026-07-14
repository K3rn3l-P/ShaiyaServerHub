# Fatti sul server (aggiornare a mano se cambia l'infrastruttura)

## Sistema
- Windows Server 2025 Datacenter. Il server di gioco vive in `C:\ShaiyaServer\`, il sito web in `C:\xampp\`.
- ShaiyaServerHub gira SUL server (localhost = server di produzione).

## Servizi Windows
| Servizio | Ruolo |
|---|---|
| ps_game | Server di gioco (porta 30800) |
| ps_login | Server di login (porta 30810) |
| ps_session | Gestione sessioni (porte interne 30900/30901) |
| ps_dbAgent | Agente database |
| ps_gameLog | Log partite |
| ps_userLog | Log utenti |
| PSM_AgentServer / PSM_AgentClient | Agent/proxy gestione, canale comandi PSM (porte 40900/40901, SOLO localhost) |
| MSSQLSERVER | SQL Server |
| Apache2.4 | Web server (XAMPP) |
| sshd | OpenSSH — accesso amministrativo, INTOCCABILE |

Configurazione: `MaxConnection=500` su ps_game/ps_login. `GamePublicIP` pubblicato via Tailscale.

## Porte
| Porta | Servizio | Esposizione attesa |
|---|---|---|
| 30800 | Gioco (ps_game) | Pubblica |
| 30810 | Login (ps_login) | Pubblica |
| 30900/30901/30911/30912 | Interne ps_* (sessione/log) | SOLO localhost |
| 40900/40901 | Canale comandi PSM | SOLO localhost — NON deve mai comparire come esposta pubblicamente |
| 22 | SSH (amministrazione) | LAN/gestione — INTOCCABILE |
| 3389 | RDP (amministrazione) | LAN — INTOCCABILE |
| 80/443 | Web (Apache/XAMPP) | Pubblica |
| 1433 | SQL Server | Solo LAN/localhost, MAI 0.0.0.0 |
| 22000 | Syncthing | LAN |

Default inbound del firewall Windows = **Block** su tutti i profili (Domain/Private/Public): solo le
regole esplicite (`Shaiya-*`, `OpenSSH SSH Server`) aprono le porte sopra.

## Regole firewall note (gestite da `functions\windows-firewall\ps1\Apply-ShaiyaFirewall.ps1`)
`Shaiya-Game` (30800,30810) · `Shaiya-Web` (80,443) · `Shaiya-SQL-LAN` (1433, solo LAN) ·
`Shaiya-RDP-LAN` (3389, solo LAN) · `OpenSSH SSH Server` (22) · `Shaiya-GhidraMCP-8787` ·
`Syncthing` · `Tailscale-In`.

## Anti-cheat / protezioni di gioco
- `GMSecurity.ini` (`C:\ShaiyaServer\PSM_Client\Bin\Data\`): segreti (chiavi crittografiche, MAI in
  chiaro qui), `IP_WHITELIST` admin, `ANTI_TAMPER` (kick/ban su debugger/cheat-process/dll-injection/
  memory-patch), sistema HWID.
- `EtainShield.ini`: `AntiSpeedHack`, `AntiRangeHack`, `AntiCutting`, `AntiDupe` — ognuno con
  `ViolationLimit`/`KickOnViolation`/`BanOnViolation`/`LogOnly`. NOTA: i commenti nel file segnalano
  falsi positivi noti (es. dist. sotto lag) → non trattare ogni violazione come attacco certo.
