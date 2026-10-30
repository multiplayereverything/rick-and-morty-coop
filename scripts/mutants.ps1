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
    'noopeningmute' = 'latejoin' # a player joining mid-scene hears that scene's opening lines again
    'nolooknearest' = 'latejoin' # characters look only at their own copy's player
    'noheadlessteleport' = 'flat' # without a headset, teleporting stays off after a scene load
    'nospawnwait' = 'spawns'  # a client drops a spawn announcement it has nothing to copy for, then removes its own late copy
    'nospawntemplate' = 'garage' # a part only the host's game made never appears on a client that has none out
    'nolaundryunparent' = 'garage' # the laundry zone's game error (deprecated LevelController) on a client
    'noattachwait' = 'garage' # a client registers its own attached computer parts and removes them as made only here
    'noattachsync' = 'garage' # a part put into or taken out of an attach point moves on that player's copy only
    'nodeathobjects' = 'laundry' # the death phone and its button are shared, so one player's answer revives both
    'nodeathteleport' = 'laundry' # a respawn's teleport out of the death area runs as a shared story event on the host
    'nowristlocal' = 'laundry' # a part on one player's wrist is put on every player's own wrist
    'nocause'   = 'story'     # story events are neither sent by the host nor held back on clients
    'nospectatorview' = 'spectator'  # cycling spectator cameras never visits the other players' views
    'noinertorder' = 'spectator'  # copying the spectator camera leaves game scripts on it (removed in an order Unity refuses)
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
if ($Only.Count -gt 0 -and @($results).Count -lt $Only.Count) { Write-Output ('MUTANTS: unknown name in ' + ($Only -join ', ')); exit 1 }
$results | ConvertTo-Json | Set-Content (Join-Path $modRoot 'artifacts/tests/mutants/summary.json')
if (@($results | Where-Object { -not $_.Caught }).Count -gt 0) { Write-Output 'MUTANTS: some fixes are not covered'; exit 1 }
Write-Output 'MUTANTS: every fix is caught by its tour'
