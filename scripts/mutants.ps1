# Proves each guarded fix matters: with the fix turned off (-rt-mutate), its tour must FAIL.
# Every Mutant("x") switch in src/ is listed here with the tour that catches it (tests/rules-test.py checks both ways).
param([string[]]$Only = @())
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$mutants = [ordered]@{
    'nohandoff' = 'handoff'   # the previous holder keeps holding when someone takes the item
    'noclaim'   = 'handoff'   # a client's grab is never sent to the host
    'nofollow'  = 'handoff'   # non-owners keep simulating their own copy instead of following the owner
}
$results = @()
foreach ($m in $mutants.Keys) {
    if ($Only.Count -gt 0 -and $m -notin $Only) { continue }
    $tour = $mutants[$m]
    $output = & (Join-Path $PSScriptRoot 'run-tour.ps1') -Tour $tour -Mutate $m 2>&1 | Out-String
    $caught = $output -match "${tour}: FAIL" -or $output -match "-${m}: FAIL"
    $first = ($output -split "`n" | Where-Object { $_ -match '^\s+- ' } | Select-Object -First 1)
    $results += [pscustomobject]@{ Mutant = $m; Tour = $tour; Caught = [bool]$caught; FirstFailure = ("$first").Trim() }
    Write-Output ("{0} ({1}): {2}  {3}" -f $m, $tour, $(if ($caught) { 'CAUGHT' } else { 'MISSED' }), ("$first").Trim())
}
$results | ConvertTo-Json | Set-Content (Join-Path $modRoot 'artifacts/tests/mutants/summary.json')
if (@($results | Where-Object { -not $_.Caught }).Count -gt 0) { Write-Output 'MUTANTS: some fixes are not covered'; exit 1 }
Write-Output 'MUTANTS: every fix is caught by its tour'
