using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Gestione pratica di Windows Firewall via cmdlet NetSecurity (PowerShell elevato).
    /// Stato profili, regole Shaiya-*, applicazione posture consigliata, audit superficie.
    /// L'Hub gira come Administrator (app.manifest) → le modifiche vanno a buon fine.
    /// </summary>
    public class FirewallManager
    {
        private const char T = '\t';

        // Rileva se simplewall sta DAVVERO filtrando: conta i filtri WFP a suo nome.
        // Il solo processo in tray non significa che filtri (può essere in pausa/disattivato).
        private const string SimplewallProbe = @"
$swProc = [bool](Get-Process simplewall -ErrorAction SilentlyContinue)
$swFilt = $false
try {
  $tmp = Join-Path $env:TEMP ('swf_'+[guid]::NewGuid().ToString('N')+'.xml')
  netsh wfp show filters file=""$tmp"" | Out-Null
  if (Test-Path $tmp) {
    [xml]$x = Get-Content $tmp -Raw
    $swFilt = ( ($x.wfpdiag.filters.item | Where-Object { $_.displayData.name -match 'simplewall' } | Measure-Object).Count -gt 0 )
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
  }
} catch {}
'SIMPROC'+[char]9+([bool]$swProc)
'SIMFILT'+[char]9+([bool]$swFilt)";

        // ── Stato profili + simplewall ───────────────────────
        public FirewallStatus GetStatus()
        {
            string ps = @"
$ErrorActionPreference='SilentlyContinue'
foreach($x in Get-NetFirewallProfile){ 'PROF'+[char]9+$x.Name+[char]9+$x.Enabled+[char]9+$x.DefaultInboundAction }
" + SimplewallProbe;
            ProcessRunner.RunPowerShellEncodedCapture(ps, out string outp);
            var st = new FirewallStatus();
            foreach (var line in Lines(outp))
            {
                var f = line.Split(T);
                if (f[0] == "PROF" && f.Length >= 4)
                    st.Profiles.Add(new FirewallProfileState
                    {
                        Name = f[1],
                        Enabled = ParseBool(f[2]),
                        DefaultInbound = f[3]
                    });
                else if (f[0] == "SIMPROC" && f.Length >= 2) st.SimplewallRunning = ParseBool(f[1]);
                else if (f[0] == "SIMFILT" && f.Length >= 2) st.SimplewallFiltering = ParseBool(f[1]);
            }
            return st;
        }

        /// <summary>Abilita/disabilita TUTTI i profili firewall. Ritorna true se ok.</summary>
        public bool SetFirewallEnabled(bool enabled, Action<string> log)
        {
            string val = enabled ? "True" : "False";
            string ps = $@"
$ErrorActionPreference='Stop'
try {{ Set-NetFirewallProfile -All -Enabled {val}; 'OK' }}
catch {{ 'ERR '+$_.Exception.Message }}";
            return RunMut(ps, log);
        }

        // ── Regole (Shaiya-* + SSH) ──────────────────────────
        public List<FirewallRuleRow> LoadRules(string lanCidr)
        {
            const string ps = @"
$ErrorActionPreference='SilentlyContinue'
$rules = Get-NetFirewallRule -Direction Inbound |
    Where-Object { $_.DisplayName -like 'Shaiya-*' -or $_.DisplayName -like '*OpenSSH*' -or $_.DisplayName -like '*SSH*' }
foreach($r in $rules){
  $pf=$r|Get-NetFirewallPortFilter
  $af=$r|Get-NetFirewallAddressFilter
  'RULE'+[char]9+$r.DisplayName+[char]9+$r.Enabled+[char]9+$pf.Protocol+[char]9+(($pf.LocalPort)-join ',')+[char]9+(($af.RemoteAddress)-join ',')+[char]9+$r.Action
}";
            ProcessRunner.RunPowerShellEncodedCapture(ps, out string outp);
            var list = new List<FirewallRuleRow>();
            foreach (var line in Lines(outp))
            {
                var f = line.Split(T);
                if (f[0] != "RULE" || f.Length < 7) continue;
                string remote = f[5];
                var row = new FirewallRuleRow
                {
                    DisplayName = f[1],
                    Enabled = ParseBool(f[2]),
                    Protocol = f[3],
                    LocalPort = f[4],
                    RemoteAddress = remote,
                    Action = f[6],
                    IsManaged = f[1].StartsWith("Shaiya-", StringComparison.OrdinalIgnoreCase),
                    ScopeLabel = ScopeLabel(remote, lanCidr)
                };
                list.Add(row);
            }
            return list.OrderByDescending(r => r.IsManaged).ThenBy(r => r.DisplayName).ToList();
        }

        public bool SetRuleEnabled(string displayName, bool enabled, Action<string> log)
        {
            string val = enabled ? "True" : "False";
            string ps = $@"
$ErrorActionPreference='Stop'
try {{ Set-NetFirewallRule -DisplayName {PsStr(displayName)} -Enabled {val}; 'OK' }}
catch {{ 'ERR '+$_.Exception.Message }}";
            return RunMut(ps, log);
        }

        /// <summary>Rimuove SOLO regole Shaiya-* (guardia di sicurezza).</summary>
        public bool RemoveRule(string displayName, Action<string> log)
        {
            if (!displayName.StartsWith("Shaiya-", StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke("Rimozione consentita solo per regole Shaiya-*.");
                return false;
            }
            string ps = $@"
$ErrorActionPreference='Stop'
try {{ Get-NetFirewallRule -DisplayName {PsStr(displayName)} | Where-Object {{ $_.DisplayName -like 'Shaiya-*' }} | Remove-NetFirewallRule; 'OK' }}
catch {{ 'ERR '+$_.Exception.Message }}";
            return RunMut(ps, log);
        }

        /// <summary>Crea una regola inbound. name senza prefisso → viene aggiunto 'Shaiya-Custom-'.</summary>
        public bool AddRule(string name, string protocol, string localPort, string action,
                            string scope, string lanCidr, Action<string> log)
        {
            string dn = name.StartsWith("Shaiya-", StringComparison.OrdinalIgnoreCase)
                ? name : "Shaiya-Custom-" + name;

            string remoteArg = "";
            if (string.Equals(scope, "LAN", StringComparison.OrdinalIgnoreCase))
                remoteArg = $" -RemoteAddress {PsStr(lanCidr)}";
            else if (!string.Equals(scope, "Any", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(scope))
                remoteArg = $" -RemoteAddress {PsStr(scope)}";

            string ps = $@"
$ErrorActionPreference='Stop'
try {{
  New-NetFirewallRule -DisplayName {PsStr(dn)} -Direction Inbound -Action {action} -Protocol {protocol} -LocalPort {PsStr(localPort)} -Profile Any{remoteArg} | Out-Null
  'OK'
}} catch {{ 'ERR '+$_.Exception.Message }}";
            return RunMut(ps, log);
        }

        // Nome dell'UNICA regola usata dal tool AI "temp_block_ip" — MAI una regola per IP (a scala,
        // centinaia/migliaia di regole individuali degradano New-NetFirewallRule/l'enumerazione via
        // PowerShell, un limite noto del layer di management, non del filtro WFP a kernel in sé). Un
        // solo -RemoteAddress con l'insieme dinamico degli IP bloccati costa O(1) regole indipendente
        // da quanti IP contiene — stesso principio degli ipset su Linux.
        public const string TempBlockRuleName = "Shaiya-AI-TempBlock";

        /// <summary>
        /// Crea/aggiorna l'UNICA regola di blocco IP temporaneo con l'insieme COMPLETO degli IP
        /// attualmente attivi (chiamata con l'intero elenco ad ogni add/remove, non incrementale — più
        /// semplice e senza rischio di drift tra stato in-memory e regola reale). Lista vuota → rimuove
        /// la regola (nessun errore se già assente).
        /// </summary>
        public bool SyncTempBlockRule(IEnumerable<string> activeIps, Action<string> log)
        {
            var ips = (activeIps ?? Enumerable.Empty<string>()).Distinct().ToList();
            if (ips.Count == 0)
            {
                string psRemove = $@"
$ErrorActionPreference='SilentlyContinue'
Get-NetFirewallRule -DisplayName {PsStr(TempBlockRuleName)} | Remove-NetFirewallRule
'OK'";
                return RunMut(psRemove, log);
            }

            string addrList = string.Join(",", ips.Select(PsStr));
            string ps = $@"
$ErrorActionPreference='Stop'
try {{
  $existing = Get-NetFirewallRule -DisplayName {PsStr(TempBlockRuleName)} -ErrorAction SilentlyContinue
  if ($existing) {{
    $existing | Set-NetFirewallRule -RemoteAddress @({addrList})
  }} else {{
    New-NetFirewallRule -DisplayName {PsStr(TempBlockRuleName)} -Direction Inbound -Action Block -Protocol TCP -RemoteAddress @({addrList}) -Profile Any | Out-Null
  }}
  'OK'
}} catch {{ 'ERR '+$_.Exception.Message }}";
            return RunMut(ps, log);
        }

        /// <summary>
        /// Modifica una regola esistente (per DisplayName): protocollo, porte, azione, ambito.
        /// Il nome resta l'identità della regola e non cambia.
        /// </summary>
        public bool UpdateRule(string displayName, string protocol, string localPort, string action,
                               string scope, string lanCidr, Action<string> log)
        {
            string remote;
            if (string.Equals(scope, "LAN", StringComparison.OrdinalIgnoreCase)) remote = lanCidr;
            else if (string.Equals(scope, "Any", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(scope)) remote = "Any";
            else remote = scope;

            string ps = $@"
$ErrorActionPreference='Stop'
try {{
  $r = Get-NetFirewallRule -DisplayName {PsStr(displayName)} -ErrorAction Stop
  $r | Set-NetFirewallRule -Action {action} -RemoteAddress {PsStr(remote)}
  $r | Set-NetFirewallPortFilter -Protocol {protocol} -LocalPort {PsStr(localPort)}
  'OK'
}} catch {{ 'ERR '+$_.Exception.Message }}";
            return RunMut(ps, log);
        }

        /// <summary>Esegue lo script posture consigliata (Apply-ShaiyaFirewall.ps1) con LanCidr.</summary>
        public int ApplyPosture(string scriptPath, string lanCidr, Action<string> log)
        {
            string ps = $@"& {PsStr(scriptPath)} -LanCidr {PsStr(lanCidr)}";
            return ProcessRunner.RunPowerShellEncoded(ps, log);
        }

        // ── Audit superficie di rete ─────────────────────────
        public List<PortAuditRow> NetworkAudit(string lanCidr, out bool firewallOn, out bool simplewall)
        {
            string ps = @"
$ErrorActionPreference='SilentlyContinue'
$active = Get-NetFirewallProfile | Where-Object { $_.Enabled -eq $true }
'FW'+[char]9+([bool]$active)
" + SimplewallProbe + @"
$listen = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalAddress -in @('0.0.0.0','::') }
$allow = Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -ErrorAction SilentlyContinue
$entries = foreach($r in $allow){
  $pf=$r|Get-NetFirewallPortFilter; $af=$r|Get-NetFirewallAddressFilter; $app=$r|Get-NetFirewallApplicationFilter
  [pscustomobject]@{
    Proto   = [string]$pf.Protocol
    Ports   = @($pf.LocalPort)
    Remote  = (($af.RemoteAddress)-join ',')
    Program = [string]$app.Program
  }
}
$ports = $listen | Select-Object -ExpandProperty LocalPort -Unique | Sort-Object { [int]$_ }
foreach($p in $ports){
  $conn = $listen | Where-Object { $_.LocalPort -eq $p } | Select-Object -First 1
  $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
  $pn = $proc.ProcessName; if(-not $pn){ $pn = '?' }
  $ppath = ''
  try { $ppath = [string]$proc.Path } catch {}
  $scopes = @()
  foreach($e in $entries){
    if( ($e.Proto -ne 'TCP') -and ($e.Proto -ne 'Any') ){ continue }
    $portExact = ($e.Ports -contains ""$p"")
    $portAny   = ($e.Ports -contains 'Any')
    $progAny   = ([string]::IsNullOrEmpty($e.Program) -or $e.Program -eq 'Any')
    $progMatch = ($ppath -ne '') -and (-not $progAny) -and ($e.Program -ieq $ppath)
    # Verdetto per-porta: regola con porta ESPLICITA (programma any o coincidente),
    # oppure regola Any-porta SOLO se il programma coincide col processo in ascolto.
    # Le regole Any-porta con Program=Any (App Installer, Tailscale-In, ...) sono
    # scoped via package/interfaccia a livello WFP e NON espongono porte arbitrarie.
    $match = $false
    if($portExact){ if($progAny -or $progMatch){ $match = $true } }
    elseif($portAny -and $progMatch){ $match = $true }
    if($match){ $scopes += $e.Remote }
  }
  'PORT'+[char]9+$p+[char]9+$pn+[char]9+(($scopes)-join ';')
}";
            ProcessRunner.RunPowerShellEncodedCapture(ps, out string outp);
            firewallOn = false; simplewall = false;
            var list = new List<PortAuditRow>();
            foreach (var line in Lines(outp))
            {
                var f = line.Split(T);
                if (f[0] == "FW" && f.Length >= 2) firewallOn = ParseBool(f[1]);
                else if (f[0] == "SIMFILT" && f.Length >= 2) simplewall = ParseBool(f[1]);
                else if (f[0] == "PORT" && f.Length >= 3)
                {
                    int port = int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pp) ? pp : 0;
                    string scope = f.Length >= 4 ? f[3] : "";
                    list.Add(Classify(port, f[2], scope, firewallOn, lanCidr));
                }
            }
            return list;
        }

        // ── Catalogo porte note + classificazione rischio ────
        private enum Cat { PublicOk, LanOnly, LocalOnly, Unknown }

        private static PortAuditRow Classify(int port, string proc, string scope, bool fwOn, string lanCidr)
        {
            GetCatalog(port, out Cat cat, out string purpose);

            bool hasAny = Regex.IsMatch(scope ?? "", @"(^|[;,])\s*Any\s*($|[;,])", RegexOptions.IgnoreCase);
            bool hasScoped = !string.IsNullOrWhiteSpace(scope) && !OnlyAny(scope);

            string exposure;
            if (hasAny) exposure = "PUBBLICA";
            else if (hasScoped) exposure = "LAN";
            else exposure = fwOn ? "PROTETTA" : "ESPOSTA";

            string risk, note = "";
            bool exposedOutside = exposure == "PUBBLICA" || exposure == "ESPOSTA";
            switch (cat)
            {
                case Cat.PublicOk:
                    risk = "OK";
                    if (exposure == "PROTETTA") { risk = "ATTENZIONE"; note = "porta pubblica prevista ma nessuna regola: non raggiungibile"; }
                    break;
                case Cat.LanOnly:
                    risk = exposedOutside ? "RISCHIO" : "OK";
                    if (exposedOutside) note = "gestione: dovrebbe stare solo su LAN";
                    break;
                case Cat.LocalOnly:
                    risk = exposedOutside ? "RISCHIO" : (exposure == "LAN" ? "ATTENZIONE" : "OK");
                    if (exposedOutside) note = "servizio interno: non deve essere esposto";
                    break;
                default:
                    risk = exposedOutside ? "ATTENZIONE" : "OK";
                    if (exposedOutside) note = "porta non catalogata esposta: verifica";
                    break;
            }
            if (exposure == "ESPOSTA" && string.IsNullOrEmpty(note))
                note = "firewall OFF: raggiungibile dall'esterno";

            return new PortAuditRow
            {
                Port = port,
                Process = string.IsNullOrEmpty(proc) ? "?" : proc,
                Purpose = purpose,
                Exposure = exposure,
                Risk = risk,
                Note = note
            };
        }

        private static void GetCatalog(int port, out Cat cat, out string purpose)
        {
            switch (port)
            {
                case 80: case 443: cat = Cat.PublicOk; purpose = "Web sito (pubblico)"; return;
                case 30800: cat = Cat.PublicOk; purpose = "ps_login (gioco, pubblico)"; return;
                case 30810: cat = Cat.PublicOk; purpose = "ps_game (gioco, pubblico)"; return;
                case 22: cat = Cat.LanOnly; purpose = "SSH (gestione)"; return;
                case 3389: cat = Cat.LanOnly; purpose = "RDP (gestione)"; return;
                case 1433: cat = Cat.LanOnly; purpose = "SQL Server (gestione)"; return;
                case 8787: cat = Cat.LanOnly; purpose = "Ghidra MCP (reverse)"; return;
                case 22000: cat = Cat.LanOnly; purpose = "Syncthing (sync NAS)"; return;
                case 135: cat = Cat.LocalOnly; purpose = "RPC Endpoint Mapper (Windows)"; return;
                case 445: cat = Cat.LocalOnly; purpose = "SMB (condivisione file)"; return;
                case 5357: cat = Cat.LocalOnly; purpose = "WSDAPI (Windows)"; return;
                case 5985: cat = Cat.LocalOnly; purpose = "WinRM HTTP (gestione remota)"; return;
                case 47001: cat = Cat.LocalOnly; purpose = "WinRM listener (Windows)"; return;
                case 30900: case 30901: case 30911: case 30912:
                    cat = Cat.LocalOnly; purpose = "ps_* interno (solo localhost)"; return;
                case 40900: case 40901:
                    cat = Cat.LocalOnly; purpose = "PSM_Agent canale comandi (solo localhost)"; return;
                default:
                    if (port >= 49152) { cat = Cat.LocalOnly; purpose = "RPC dinamico (Windows)"; return; }
                    cat = Cat.Unknown; purpose = "—"; return;
            }
        }

        // ── Helper ───────────────────────────────────────────
        private static bool RunMut(string ps, Action<string> log)
        {
            int rc = ProcessRunner.RunPowerShellEncodedCapture(ps, out string outp);
            bool ok = rc == 0 && outp.IndexOf("OK", StringComparison.Ordinal) >= 0
                      && outp.IndexOf("ERR", StringComparison.Ordinal) < 0;
            foreach (var l in Lines(outp))
                if (l.StartsWith("ERR")) log?.Invoke(l);
            return ok;
        }

        private static IEnumerable<string> Lines(string s) =>
            (s ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

        private static bool ParseBool(string s) =>
            string.Equals(s, "True", StringComparison.OrdinalIgnoreCase) || s == "1";

        private static bool OnlyAny(string scope) =>
            scope.Split(';', ',').Select(x => x.Trim())
                 .Where(x => x.Length > 0)
                 .All(x => string.Equals(x, "Any", StringComparison.OrdinalIgnoreCase));

        private static string ScopeLabel(string remote, string lanCidr)
        {
            if (string.IsNullOrWhiteSpace(remote) || OnlyAny(remote)) return L.FwScopePublic;
            return L.FwScopeRestricted(remote);
        }

        /// <summary>Quota una stringa per PowerShell (single-quote, raddoppia gli apici).</summary>
        private static string PsStr(string s) => "'" + (s ?? "").Replace("'", "''") + "'";
    }
}
