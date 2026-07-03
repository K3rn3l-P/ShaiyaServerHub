# 16_network_exposure.ps1
# Port-security / riduzione superficie (anti-DDoS, anti-enumerazione, protezione canale comandi).
# Una porta in ascolto e' "esposta" solo se il firewall ha un allow inbound TCP con RemoteAddress=Any.
# Controlli:
#   [A] Default inbound = Block su ogni profilo abilitato (base della posture).
#   [B] CRITICO/FAIL: canale comandi PSM (40900/40901) + porte interne ps_* (30900/30901/30911/30912)
#       NON devono avere allow pubblico: vanno raggiunte solo da localhost/processi locali.
#   [C] WARN: porte admin (135/445/3389/5985/5986/1433, ≠1433) con allow pubblico -> restringere a LAN.
#   [D] WARN: qualsiasi ALTRA porta TCP pubblica oltre a quelle attese (game 30800/30810, web 80/443)
#       -> fuga (es. Syncthing/MSMPI/app varie): restringere a LAN o disabilitare la regola.
#   WARN anche se Windows Firewall e' interamente disattivo (enforcement solo via simplewall: non verificabile qui).
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'

$gamePorts     = if ($Ctx.GamePorts) { @($Ctx.GamePorts) } else { 30800, 30810 }
$webPorts      = if ($Ctx.WebPorts)  { @($Ctx.WebPorts)  } else { 80, 443 }
$adminPorts    = 135, 445, 3389, 5985, 5986, 1433
$internalPorts = 40900, 40901, 30900, 30901, 30911, 30912   # comandi PSM_Agent + interni ps_*: MAI pubblici
$expectedPublic = @($gamePorts) + @($webPorts)
$loop = @('127.0.0.1', '::1')

$listen = Get-NetTCPConnection -State Listen | Select-Object LocalAddress, LocalPort
$fw = Get-NetFirewallProfile | Select-Object Name, Enabled, DefaultInboundAction
$fwAllOff = -not ($fw | Where-Object { $_.Enabled })

$allowRules = Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -ErrorAction SilentlyContinue

# regole TCP pubbliche (RemoteAddress=Any) -> set di porte numeriche aperte al mondo (+ nomi regola)
$publicTcpPorts = @{}
foreach ($r in $allowRules) {
    $pf = $r | Get-NetFirewallPortFilter
    if ($pf.Protocol -ne 'TCP') { continue }
    $af = $r | Get-NetFirewallAddressFilter
    if (@($af.RemoteAddress) -notcontains 'Any') { continue }
    foreach ($lp in @($pf.LocalPort)) {
        if ($lp -match '^\d+$') {
            if (-not $publicTcpPorts.ContainsKey([int]$lp)) { $publicTcpPorts[[int]$lp] = @() }
            $publicTcpPorts[[int]$lp] += $r.DisplayName
        }
    }
}

function Is-PublicPort([int]$port) { return $publicTcpPorts.ContainsKey($port) }

# [B] interni/comandi esposti pubblicamente -> CRITICO
$internalExposed = @()
foreach ($p in $internalPorts) { if (Is-PublicPort $p) { $internalExposed += $p } }

# [C] porte admin (≠1433) in ascolto su non-loopback con allow pubblico
$adminListen = $listen | Where-Object { $_.LocalPort -in $adminPorts } | Sort-Object LocalPort, LocalAddress -Unique
$adminNonLoop = $adminListen | Where-Object { $_.LocalPort -ne 1433 -and $_.LocalAddress -notin $loop } |
                Select-Object -ExpandProperty LocalPort -Unique
$adminExposed = @()
foreach ($p in $adminNonLoop) { if (Is-PublicPort ([int]$p)) { $adminExposed += $p } }

# [D] porte TCP pubbliche impreviste (oltre game+web)
$unexpected = @()
foreach ($p in ($publicTcpPorts.Keys | Sort-Object)) {
    if ($p -notin $expectedPublic) { $unexpected += ('{0}({1})' -f $p, (($publicTcpPorts[$p] | Select-Object -Unique) -join '|')) }
}

# [A] default inbound non-Block su profili attivi
$badDefault = $fw | Where-Object { $_.Enabled -and $_.DefaultInboundAction -ne 'Block' } | Select-Object -ExpandProperty Name

$cmdListenStr = ($listen | Where-Object { $_.LocalPort -in $internalPorts } |
                 ForEach-Object { "$($_.LocalPort)@$($_.LocalAddress)" } | Sort-Object -Unique) -join ', '
$detail = "default_inbound=[$((($fw | Where-Object {$_.Enabled} | ForEach-Object {"$($_.Name):$($_.DefaultInboundAction)"}) -join ','))]"
$detail += " ; public_tcp=[$((($publicTcpPorts.Keys | Sort-Object) -join ','))]"
$detail += " ; cmd/intern_listen=[$cmdListenStr]"

$status = 'PASS'
if ($internalExposed.Count) {
    $status = 'FAIL'
    $detail += " ; CRITICO: canale comandi/interni ESPOSTI pubblicamente: $($internalExposed -join ',') (devono essere solo localhost!)"
}
if ($fwAllOff) {
    if ($status -ne 'FAIL') { $status = 'WARN' }
    $detail += ' ; Windows Firewall TUTTI i profili OFF (enforcement solo via simplewall - non verificabile qui)'
} else {
    if ($badDefault) {
        if ($status -ne 'FAIL') { $status = 'WARN' }
        $detail += " ; default inbound NON Block su: $($badDefault -join ',')"
    }
    if ($adminExposed.Count) {
        if ($status -ne 'FAIL') { $status = 'WARN' }
        $detail += " ; porte admin con allow PUBBLICO: $($adminExposed -join ',') (restringere a LAN/Tailscale)"
    }
    if ($unexpected.Count) {
        if ($status -ne 'FAIL') { $status = 'WARN' }
        $detail += " ; porte TCP pubbliche IMPREVISTE (oltre game/web): $($unexpected -join ', ') (restringere a LAN o disabilitare)"
    }
}
if ($status -eq 'PASS') {
    $detail += ' ; OK: default-block, comandi/interni non esposti, solo game/web pubblici'
}

[pscustomobject]@{ Name = '16_network_exposure'; Status = $status; Detail = $detail }
