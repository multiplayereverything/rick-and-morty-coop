# Proves each guarded fix matters: with the fix turned off (-rt-mutate), its tour must FAIL.
# Every Mutant("x") switch in src/ is listed here with the tour that catches it (tests/rules-test.py checks both ways).
param([string[]]$Only = @())
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$mutants = [ordered]@{
    'nohandoff' = 'handoff'   # the previous holder keeps holding when someone takes the item
    'noclaim'   = 'handoff'   # a client's grab is never sent to the host
    'nofollow'  = 'handoff'   # non-owners keep simulating their own copy instead of following the owner
    'nocontrol' = 'controls'  # levers and doors ignore the owner's position and lock state
    'nobutton'  = 'controls'  # a button pushed here is never sent to the other players
    'nospawnmatch' = 'spawns' # a client never adopts its own copy of something both games made, so there are two
    'nospawnremove' = 'spawns' # something only a client's game made stays there
    'nodespawn' = 'spawns'    # an item destroyed in the host's game stays on the client
    'noflatreach' = 'flat'    # a flat-screen click does not reach for the item in view, so items are hard to grab
    'noflatbuttons' = 'flat'  # flat mode's trigger and menu keys never reach the game's hand controllers
    'noenemysync' = 'enemies' # clients run their own enemy waves instead of showing the host's
    'nodeathlocal' = 'death'  # a client's death area and death events are treated as shared, so it is pulled back or the host dies
    'noenemytarget' = 'enemies' # enemies only aim at and hit the host
    'nobosssync' = 'boss'     # each copy's final boss changes state on its own
    'nostagerepair' = 'boss'  # a client whose garage load reset its stage is not put back on the host's
    'nocause'   = 'story'     # story events are neither sent by the host nor held back on clients
    'nospectatorview' = 'spectator'  # cycling spectator cameras never visits the other players' views
    'nostage'   = 'story'     # clients ignore the host's progression stage
    'noarea'    = 'story'     # clients do not mirror the host's garage areas
    'noscenefollow' = 'story' # clients neither follow the host's scene loads nor hold their own back
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
