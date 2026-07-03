<#
.SYNOPSIS
  Script UNICO: builda la Release (o Debug) E copia gli script di default
  (DefaultScripts\) nell'output, iniettando le password reali dal .env.
  Path RELATIVI a questo file → il repo può stare in qualsiasi cartella.

.USAGE
  powershell -File Build-And-Copy.ps1            # Release
  powershell -File Build-And-Copy.ps1 -Config Debug
  (oppure doppio click su build-and-copy.bat nella root del repo)
#>
param([string]$Config = 'Release')

$ErrorActionPreference = 'Stop'

# ── Percorsi (relativi a questo script) ──────────────────────────────
$proj    = $PSScriptRoot                                   # ...\ShaiyaServerHub\ShaiyaServerHub
$repo    = Split-Path $proj -Parent                        # ...\ShaiyaServerHub  (root repo)
$sln     = Join-Path $repo 'ShaiyaServerHub.sln'
$outDir  = Join-Path $proj ("bin\{0}\ShaiyaServerHub" -f $Config)
$srcDir  = Join-Path $proj 'DefaultScripts'
$envFile = Join-Path $repo '.env'

Write-Host "== ShaiyaServerHub :: Build-And-Copy ($Config) =="

# ── 1) Build ─────────────────────────────────────────────────────────
Write-Host "`n[1/3] dotnet build $Config ..."
& dotnet build "$sln" -c $Config -nologo
if ($LASTEXITCODE -ne 0) { Write-Host "`n>>> BUILD FALLITA (exit $LASTEXITCODE)" -ForegroundColor Red; exit 1 }

# ── 2) Copia DefaultScripts nell'output (preserva struttura) ─────────
Write-Host "`n[2/3] Copia DefaultScripts -> $outDir"
if (-not (Test-Path -LiteralPath $srcDir)) { Write-Host "DefaultScripts non trovata: $srcDir" -ForegroundColor Yellow; exit 1 }
Get-ChildItem -LiteralPath $srcDir -Recurse -File | Where-Object { $_.Name -ne '.gitkeep' } | ForEach-Object {
    $rel = $_.FullName.Substring($srcDir.Length).TrimStart('\', '/')
    $dst = Join-Path $outDir $rel
    $dstDir = Split-Path $dst -Parent
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
    Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
}

# ── 3) Inietta le password dal .env + copia il .env ─────────────────
Write-Host "`n[3/3] Password dal .env"
if (Test-Path -LiteralPath $envFile) {
    $vars = @{}
    Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^\s*[^#].*=' } | ForEach-Object {
        $parts = $_ -split '=', 2
        $vars[$parts[0].Trim()] = $parts[1].Trim()
    }
    $map = @{
        '<<CAMBIA_PASSWORD_PSMCmdOwner>>'     = $vars['PSMCmdOwner_PASSWORD']
        '<<CAMBIA_PASSWORD_ShaiyaTaskAgent>>' = $vars['ShaiyaTaskAgent_PASSWORD']
    }
    $fnDir = Join-Path $outDir 'functions\sql'
    if (Test-Path -LiteralPath $fnDir) {
        Get-ChildItem -LiteralPath $fnDir -Filter *.sql | ForEach-Object {
            $c = Get-Content -LiteralPath $_.FullName -Raw
            $changed = $false
            foreach ($k in $map.Keys) {
                if ($map[$k] -and $c.Contains($k)) { $c = $c.Replace($k, $map[$k]); $changed = $true }
            }
            if ($changed) { Set-Content -LiteralPath $_.FullName -Value $c -NoNewline -Encoding UTF8 }
        }
    }
    Copy-Item -LiteralPath $envFile -Destination (Join-Path $outDir '.env') -Force
    Write-Host "      Password iniettate + .env copiato." -ForegroundColor Green
}
else {
    Write-Host "      .env assente: gli script install restano con i placeholder." -ForegroundColor Yellow
}

Write-Host "`n== FATTO. Cartella pronta per copia sul server: $outDir ==" -ForegroundColor Green
