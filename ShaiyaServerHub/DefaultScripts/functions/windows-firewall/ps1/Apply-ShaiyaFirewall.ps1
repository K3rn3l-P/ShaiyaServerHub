<#
.SYNOPSIS
    Configura Windows Firewall per un server Shaiya (installazione pulita).
.DESCRIPTION
    Posture:
      - PUBBLICO (Any):  porte gioco (30800/30810) + web (80/443).
      - SOLO LAN:        SSH(22), RDP(3389), SQL(1433), Ghidra MCP(8787).
      - Default inbound = Block; tutto cio' che non e' esplicitamente permesso e' bloccato.
      - Disabilita i gruppi inbound inutili su un server headless (riduzione superficie).
    Idempotente: puo' essere rieseguito. NON elimina regole built-in (le disabilita: si rigenererebbero).
.NOTES
    ESEGUIRE COME AMMINISTRATORE. SSH/RDP/SQL restano permessi SOLO dalla LAN ($LanCidr):
    assicurati che il tuo PC di gestione sia in quella rete prima di eseguire (rischio lockout).
#>
[CmdletBinding()]
param(
    [string]$LanCidr    = '10.10.0.0/16',   # la TUA rete locale (PC di gestione + server)
    [int[]] $GamePorts  = @(30800, 30810),  # ps_game / ps_login - pubblici
    [int[]] $WebPorts   = @(80, 443),       # sito xampp - pubblico
    [int]   $RdpPort    = 3389,
    [int]   $SqlPort    = 1433,
    [int]   $GhidraPort = 8787,
    [switch]$SkipDisableGroups               # passa per NON disabilitare i gruppi inutili
)
$ErrorActionPreference = 'Stop'
function Info($m) { Write-Host $m -ForegroundColor Cyan }

# --- Verifica privilegi amministrativi ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) { Write-Error 'Devi eseguire questo script come Amministratore.'; exit 1 }

# --- 0. Profili attivi + default inbound = Block ---
Set-NetFirewallProfile -Profile Domain, Private, Public -Enabled True -DefaultInboundAction Block -DefaultOutboundAction Allow
Info 'Profili firewall abilitati; default inbound = Block.'

# --- 1. Pulisci regole Shaiya-* preesistenti (idempotenza) ---
Get-NetFirewallRule -DisplayName 'Shaiya-*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule

# --- 2. PUBBLICO: gioco + web ---
New-NetFirewallRule -DisplayName 'Shaiya-Game' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $GamePorts -Profile Any | Out-Null
New-NetFirewallRule -DisplayName 'Shaiya-Web'  -Direction Inbound -Action Allow -Protocol TCP -LocalPort $WebPorts  -Profile Any | Out-Null
Info "Game [$($GamePorts -join ',')] e Web [$($WebPorts -join ',')] -> pubblici (Any)."

# --- 3. SOLO LAN: RDP, SQL, Ghidra MCP ---
New-NetFirewallRule -DisplayName 'Shaiya-RDP-LAN'        -Direction Inbound -Action Allow -Protocol TCP -LocalPort $RdpPort    -RemoteAddress $LanCidr -Profile Any | Out-Null
New-NetFirewallRule -DisplayName 'Shaiya-SQL-LAN'        -Direction Inbound -Action Allow -Protocol TCP -LocalPort $SqlPort    -RemoteAddress $LanCidr -Profile Any | Out-Null
New-NetFirewallRule -DisplayName 'Shaiya-GhidraMCP-8787' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $GhidraPort -RemoteAddress $LanCidr -Profile Any | Out-Null
Info "RDP/$RdpPort, SQL/$SqlPort, Ghidra/$GhidraPort -> solo LAN $LanCidr."

# --- 4. SSH (OpenSSH) -> solo LAN ---
$sshRules = Get-NetFirewallRule -DisplayName '*OpenSSH*' -ErrorAction SilentlyContinue  # NB: -DisplayName e -Direction insieme = AmbiguousParameterSet
if ($sshRules) {
    # NB: impostare per -DisplayName (forma verificata funzionante). Evitare il pipe e -Name:
    # danno "Parameter set cannot be resolved" / "InputObjectNotBound".
    foreach ($r in $sshRules) { Set-NetFirewallRule -DisplayName $r.DisplayName -RemoteAddress $LanCidr }
    Info 'Regole OpenSSH esistenti ristrette a LAN.'
} else {
    New-NetFirewallRule -DisplayName 'Shaiya-SSH-LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 22 -RemoteAddress $LanCidr -Profile Any | Out-Null
    Info 'Creata regola SSH (22) -> solo LAN.'
}

# --- 5. Riduzione superficie: disabilita gruppi inbound inutili (server headless) ---
if (-not $SkipDisableGroups) {
    $useless = @(
        'Cast to Device functionality', 'Windows Feature Experience Pack', 'mDNS',
        'WLAN Service - WFD Services Kernel Mode Driver Rules',
        'WLAN Service - WFD Application Services Platform Coordination Protocol (Uses UDP)',
        'Delivery Optimization', 'Microsoft Edge WebView2 Runtime', 'Microsoft Media Foundation Network Source',
        'AllJoyn Router', 'DIAL protocol server', 'Wireless Display', 'App Installer', 'Microsoft Edge',
        'Desktop App Web Viewer', 'File and Printer Sharing', 'Windows Remote Management', 'Remote Desktop'
    )
    foreach ($g in $useless) { Disable-NetFirewallRule -DisplayGroup $g -ErrorAction SilentlyContinue }
    Info 'Gruppi inbound inutili disabilitati (Core Networking preservato).'
}

# --- 5b. Bonifica regole ALLOW di terze parti/app esposte al mondo (RemoteAddress=Any) ---
# Il default inbound = Block protegge gia' tutto cio' che non ha una allow esplicita
# (es. canale comandi PSM_Agent :40900 / :40901 e tutte le porte interne ps_* 30900/30901/30911/30912
#  sono in ascolto su 0.0.0.0 ma NON esposte: nessuna allow -> raggiungibili solo da localhost).
# Qui chiudiamo le allow RESIDUE non-Shaiya che esporrebbero porte al mondo.
# PRESERVATI di proposito: Tailscale (accesso remoto) e Mesh Agent (gestione remota).

# Syncthing: deve parlare solo col NAS in LAN -> restringi a $LanCidr (NON rimuovere: serve la sync)
$syncRules = Get-NetFirewallRule -DisplayName 'Syncthing*' -ErrorAction SilentlyContinue
if ($syncRules) {
    foreach ($r in $syncRules) { Set-NetFirewallRule -DisplayName $r.DisplayName -RemoteAddress $LanCidr }
    Info "Syncthing ristretto a LAN $LanCidr (era Any)."
}

# MS-MPI (HPC, lancio processi remoto): inutile/pericoloso su game server -> disabilita
Get-NetFirewallRule -DisplayName 'MSMPI-*' -ErrorAction SilentlyContinue | Disable-NetFirewallRule -ErrorAction SilentlyContinue
Info 'Regole MSMPI disabilitate.'

# App Store/UWP rumorose + app su porta 7000 -> disabilita (server headless)
$junkApp = @('Start','Windows Security','Windows Shell Experience','Your account','Work or school account','ms-resource:ProductPkgDisplayName')
foreach ($n in $junkApp) { Get-NetFirewallRule -DisplayName $n -ErrorAction SilentlyContinue | Disable-NetFirewallRule -ErrorAction SilentlyContinue }
Get-NetFirewallRule -DisplayName '*FeedbackHub*' -ErrorAction SilentlyContinue | Disable-NetFirewallRule -ErrorAction SilentlyContinue
# qualsiasi allow inbound abilitata che apre la porta 7000 -> off
Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -ErrorAction SilentlyContinue |
    Where-Object { (($_ | Get-NetFirewallPortFilter).LocalPort) -contains '7000' } |
    Disable-NetFirewallRule -ErrorAction SilentlyContinue
Info 'Regole UWP/app rumorose e porta 7000 disabilitate (Tailscale e Mesh Agent preservati).'

# --- 6. Riepilogo ---
Write-Host "`n=== Regole Shaiya + SSH attive ===" -ForegroundColor Green
Get-NetFirewallRule -Direction Inbound -Enabled True |
    Where-Object { $_.DisplayName -like 'Shaiya-*' -or $_.DisplayName -like '*SSH*' } |
    ForEach-Object {
        $a = ($_ | Get-NetFirewallAddressFilter).RemoteAddress
        $p = ($_ | Get-NetFirewallPortFilter).LocalPort
        '{0,-28} TCP {1,-14} Remote={2}' -f $_.DisplayName, (($p) -join ','), (($a) -join ',')
    }
Write-Host "`nNOTA: se usi ANCHE simplewall, consenti 30800/30810/80/443 anche li' (entrambi i firewall devono permettere)." -ForegroundColor Yellow
Write-Host 'NOTA: verifica che SQL Server sia in ascolto sull''IP LAN (non solo 127.0.0.1) per i collegamenti dal tuo PC.' -ForegroundColor Yellow
