# 22_autoruns_baseline.ps1
# Persistence/ASEP audit via Sysinternals autorunsc (config-drift) - confronta la scansione autostart
# corrente (Run keys, servizi, driver, task, AppInit_DLLs, WMI subscriptions, ecc. - 100+ location)
# contro un baseline salvato. WARN se compaiono voci NUOVE rispetto al baseline (persistenza sospetta),
# PASS se nessuna variazione, INFO se autorunsc.exe non e' ancora scaricato o al primo run (crea il
# baseline, nessun confronto possibile ancora). Baseline volutamente NON auto-aggiornato dopo il primo
# run: una voce nuova resta WARN finche' l'admin non elimina/rigenera autoruns-baseline.csv a mano
# (mai far "sparire" da solo un drift non ancora esaminato).
# NOTA: nessun carattere non-ASCII in questo file (niente em-dash/accenti) - Windows PowerShell 5.1
# legge i .ps1 con la codepage di sistema salvo BOM esplicito, e caratteri non-ASCII in un file
# UTF-8-senza-BOM vengono corrotti (bug reale osservato: em-dash dentro una stringa ha rotto il parser).
param([hashtable]$Ctx = @{})

$ErrorActionPreference = 'SilentlyContinue'
$toolsDir = Join-Path $PSScriptRoot '..\..\tools\sysinternals'
$exe = Join-Path $toolsDir 'autorunsc.exe'
$baseline = Join-Path $toolsDir 'autoruns-baseline.csv'

if (-not (Test-Path $exe)) {
    [pscustomobject]@{ Name = '22_autoruns_baseline'; Status = 'INFO'; Detail = 'autorunsc.exe non scaricato - usa "Scarica/Aggiorna Autoruns" nella Test Suite.' }
    return
}

$current = & $exe -a * -c -h -nobanner -accepteula 2>$null
if (-not $current) {
    [pscustomobject]@{ Name = '22_autoruns_baseline'; Status = 'WARN'; Detail = 'autorunsc non ha prodotto output (permessi insufficienti o eseguibile non elevato?).' }
    return
}

if (-not (Test-Path $baseline)) {
    $current | Out-File -FilePath $baseline -Encoding UTF8
    $rows = @($current).Count
    [pscustomobject]@{ Name = '22_autoruns_baseline'; Status = 'INFO'; Detail = "baseline creato ora ($rows righe) - primo run, nessun confronto ancora possibile." }
    return
}

$old = Get-Content -Path $baseline
$new = @(Compare-Object -ReferenceObject $old -DifferenceObject $current |
         Where-Object { $_.SideIndicator -eq '=>' } | Select-Object -ExpandProperty InputObject)

if ($new.Count -gt 0) {
    $sample = ($new | Select-Object -First 5) -join ' || '
    [pscustomobject]@{ Name = '22_autoruns_baseline'; Status = 'WARN'; Detail = "$($new.Count) voce/i NUOVA/E rispetto al baseline autostart: $sample" }
} else {
    [pscustomobject]@{ Name = '22_autoruns_baseline'; Status = 'PASS'; Detail = 'nessuna variazione rispetto al baseline autostart.' }
}
