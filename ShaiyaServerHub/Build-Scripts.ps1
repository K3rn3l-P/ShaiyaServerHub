<#
.SYNOPSIS
  Copia gli script di DEFAULT (DefaultScripts\) nella cartella di output e
  inietta le password reali dal file .env (repo root) sostituendo i placeholder.
  Usato dal build (target CopyDefaultScripts) e richiamabile a mano.
.NOTES
  Repo = placeholder puliti (DefaultScripts). Output = script funzionanti.
  Il .env è in .gitignore: se manca, gli script restano con i placeholder.
#>
param(
    [Parameter(Mandatory = $true)][string]$ProjectDir,
    [Parameter(Mandatory = $true)][string]$OutDir
)
$ErrorActionPreference = 'Stop'

$src = Join-Path $ProjectDir 'DefaultScripts'
if (-not (Test-Path $src)) { Write-Host "DefaultScripts non trovata: $src"; exit 0 }

# 1) Copia ricorsiva preservando la struttura (esclude i .gitkeep)
Get-ChildItem -LiteralPath $src -Recurse -File | Where-Object { $_.Name -ne '.gitkeep' } | ForEach-Object {
    $rel = $_.FullName.Substring($src.Length).TrimStart('\', '/')
    $dst = Join-Path $OutDir $rel
    $dstDir = Split-Path $dst -Parent
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
    Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
}

# 2) Inietta i segreti dal .env (repo root = un livello sopra ProjectDir)
$envFile = Join-Path (Split-Path $ProjectDir -Parent) '.env'
if (Test-Path $envFile) {
    $vars = @{}
    Get-Content $envFile | Where-Object { $_ -match '^\s*[^#].*=' } | ForEach-Object {
        $parts = $_ -split '=', 2
        $vars[$parts[0].Trim()] = $parts[1].Trim()
    }
    $map = @{
        '<<CAMBIA_PASSWORD_PSMCmdOwner>>'     = $vars['PSMCmdOwner_PASSWORD']
        '<<CAMBIA_PASSWORD_ShaiyaTaskAgent>>' = $vars['ShaiyaTaskAgent_PASSWORD']
    }
    $fnDir = Join-Path $OutDir 'functions\sql'
    if (Test-Path $fnDir) {
        Get-ChildItem $fnDir -Filter *.sql | ForEach-Object {
            $c = Get-Content $_.FullName -Raw
            $changed = $false
            foreach ($k in $map.Keys) {
                if ($map[$k] -and $c.Contains($k)) { $c = $c.Replace($k, $map[$k]); $changed = $true }
            }
            if ($changed) { Set-Content $_.FullName $c -NoNewline -Encoding UTF8 }
        }
    }
    # Copia anche il .env nell'output (bin è in .gitignore → mai su GitHub) per riferimento.
    Copy-Item $envFile (Join-Path $OutDir '.env') -Force
    Write-Host "DefaultScripts copiati + password iniettate + .env copiato -> $OutDir"
}
else {
    Write-Host "DefaultScripts copiati (.env assente: password restano placeholder) -> $OutDir"
}
