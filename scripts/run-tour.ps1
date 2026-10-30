# Runs one scripted lab tour (small parked windows, no headset) and judges it. Results go to artifacts/tests/<tour>-results.json.
# Usage: run-tour.ps1 -Tour handoff [-Players 2] [-Seconds 180] [-Steam] [-Lag 120] [-Mutate nohandoff]
param([Parameter(Mandatory)][string]$Tour, [int]$Players = 2, [int]$Seconds = 0, [int]$Lag = 0, [switch]$Steam, [string]$Mutate = '')
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$tours = @{
    'handoff' = @{ Seconds = 200; Expect = @(
        'net.host.started@host', 'net.peer.accepted@host', 'net.welcome@c1',
        'avatar.morty', 'avatar.follow', 'avatar.moves',
        'handoff.host.grabbed@host', 'handoff.host.carried@host', 'handoff.host.released@host', 'handoff.host.sees.carry@host', 'handoff.host.grip@host', 'handoff.host.rest@host',
        'handoff.client.sees.host@c1', 'handoff.client.grip@c1', 'handoff.client.followed@c1', 'handoff.client.grabbed@c1', 'handoff.client.owner@c1', 'handoff.client.carried@c1', 'handoff.client.rest@c1',
        'tour.done') }
    'controls' = @{ Seconds = 240; Expect = @(
        'net.host.started@host', 'net.peer.accepted@host', 'net.welcome@c1',
        'controls.host.grabbed@host', 'controls.host.opened@host', 'controls.host.sees.door@host', 'controls.host.button@host', 'controls.host.closed@host',
        'controls.client.sees.door@c1', 'controls.client.grabbed@c1', 'controls.client.opened@c1', 'controls.client.button@c1', 'controls.client.sees.lock@c1',
        'tour.done') }
    'story' = @{ Seconds = 420; Expect = @(
        'net.host.started@host', 'net.peer.accepted@host', 'net.welcome@c1',
        'story.host.cause@host', 'story.host.request@host', 'story.host.stage@host', 'story.host.garage@host', 'story.host.area@host', 'story.host.sees.client@host', 'story.host.back@host',
        'story.client.cause@c1', 'story.client.held@c1', 'story.client.request@c1', 'story.client.stage@c1', 'story.client.garage@c1', 'story.client.areas@c1', 'story.client.blocked@c1', 'story.client.back@c1',
        'tour.done') }
    'spectator' = @{ Seconds = 200; Expect = @(
        'spectator.host.on@host', 'spectator.host.camera@host', 'spectator.host.watch@host', 'spectator.host.back@host',
        'spectator.client.sees.on@c1', 'spectator.client.sees.camera@c1', 'spectator.client.inert@c1', 'spectator.client.sees.off@c1', 'tour.done') }
    'flat' = @{ Seconds = 600; HostArgs = '-rt-flat'; Expect = @(
        'flat.on@host', 'flat.look@host', 'flat.walk@host', 'flat.grab@host', 'flat.buttons@host', 'flat.teleport@host',
        'flat.client.sees.grab@c1', 'tour.done') }
    'enemies' = @{ Seconds = 800; Expect = @(
        'enemies.host.area@host', 'enemies.host.wave@host', 'enemies.host.hit@host', 'enemies.host.aims@host', 'enemies.host.raycast@host', 'enemies.host.shotclient@host',
        'enemies.client.same@c1', 'enemies.client.held@c1', 'enemies.client.killed@c1', 'enemies.client.shot@c1', 'tour.done') }
    'death' = @{ Seconds = 700; Expect = @('death.host.alive@host', 'death.client.dead@c1', 'death.client.stays@c1', 'death.client.back@c1', 'tour.done') }
    'playthrough' = @{ Seconds = 900; Expect = @(
        'play.host.EggShootOut@host', 'play.host.sees.EggShootOut@host', 'play.host.Bathroom@host', 'play.host.sees.Bathroom@host', 'play.host.PetriDish@host', 'play.host.sees.PetriDish@host', 'play.host.Troy@host', 'play.host.sees.Troy@host', 'play.host.Satellite@host', 'play.host.sees.Satellite@host', 'play.host.home@host',
        'play.client.EggShootOut@c1', 'play.client.Bathroom@c1', 'play.client.PetriDish@c1', 'play.client.Troy@c1', 'play.client.Satellite@c1', 'play.client.home@c1', 'tour.done') }
    'boss' = @{ Seconds = 800; Expect = @('boss.host.on@host', 'boss.host.states@host', 'boss.client.states@c1', 'boss.client.held@c1', 'tour.done') }
    'latejoin' = @{ Seconds = 800; ClientArgs = '-rt-join-after 60'; Expect = @('latejoin.host.look@host', 'latejoin.client.muted@c1', 'latejoin.client.look@c1', 'tour.done') }
    'garage' = @{ Seconds = 400; Expect = @('garage.host.start@host', 'garage.client.start@c1', 'garage.host.loose@host', 'garage.host.repaired@host', 'garage.client.loose@c1', 'garage.client.repaired@c1', 'garage.host.attach@host', 'garage.client.attach@c1', 'garage.client.detach@c1', 'garage.host.detached@host', 'garage.client.laundry@c1', 'garage.client.errors@c1', 'tour.done') }
    'combinator' = @{ Seconds = 400; Expect = @('combinator.host.found@host', 'combinator.client.pads@c1', 'combinator.host.result@host', 'combinator.client.result@c1', 'combinator.client.single@c1', 'combinator.client.errors@c1', 'tour.done') }
    'laundry' = @{ Seconds = 1200; Expect = @('laundry.host.out@host', 'laundry.client.in@c1', 'laundry.host.washed@host', 'laundry.host.died@host', 'laundry.host.revived@host', 'laundry.client.died@c1', 'laundry.client.own@c1', 'laundry.client.revived@c1', 'laundry.host.next@host', 'laundry.host.ownrespawn@host', 'laundry.host.teleport@host', 'laundry.client.teleport@c1', 'laundry.client.watch@c1', 'laundry.client.call@c1', 'laundry.host.watch@host', 'laundry.host.fix@host', 'laundry.client.fix@c1', 'laundry.client.washed@c1', 'laundry.client.stage@c1', 'laundry.client.errors@c1', 'tour.done') }
    'spawns' = @{ Seconds = 200; Expect = @(
        'spawns.host.announced@host', 'spawns.host.despawned@host', 'spawns.host.one@host',
        'spawns.client.copied@c1', 'spawns.client.adopted@c1', 'spawns.client.removed@c1', 'spawns.client.despawned@c1', 'spawns.client.late@c1', 'tour.done') }
    'full' = @{ Seconds = 120; Expect = @('net.welcome@c1', 'net.rejected@c2', 'full.host.one@host', 'tour.done@host') }
    'portrait' = @{ Seconds = 120; Expect = @('avatar.morty', 'portrait.saved', 'tour.done') }
    'survey' = @{ Seconds = 90; Expect = @('tour.done@host') }
}
if (!$tours.ContainsKey($Tour)) { throw "Unknown tour $Tour" }
$labRoot = Join-Path $modRoot 'private'
# Only copies under private/ (lab-owned, never the real game) are touched.
$busy = Get-CimInstance Win32_Process -Filter "Name='RickAndMortyVR.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($labRoot, [StringComparison]::OrdinalIgnoreCase) }
$busy | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; Wait-Process -Id $_.ProcessId -Timeout 15 -ErrorAction SilentlyContinue }
$spec = $tours[$Tour]
if ($Seconds -le 0) { $Seconds = $spec.Seconds }
foreach ($old in @(Get-ChildItem $labRoot -Filter 'rick-and-morty-together-lab.log' -Recurse -Depth 1 -ErrorAction SilentlyContinue)) {
    for ($try = 0; $try -lt 60; $try++) {
        try { Remove-Item -LiteralPath $old.FullName -Force -ErrorAction Stop; break }
        catch { if ($try -eq 59) { throw "The lab log $($old.FullName) is still in use after 60 s." }; Start-Sleep 1 }
    }
}
$hostArgs = ''; $clientArgs = ''
if ($spec.HostArgs) { $hostArgs = ' ' + $spec.HostArgs }
if ($spec.ClientArgs) { $clientArgs = ' ' + $spec.ClientArgs }
$name = $Tour
if ($Lag -gt 0) { $hostArgs += " -rt-lag $Lag"; $clientArgs += " -rt-lag $Lag"; $name += "-lag$Lag" }
if ($Mutate) { $hostArgs += " -rt-mutate $Mutate"; $clientArgs += " -rt-mutate $Mutate"; $name = "mutants/$Tour-$($Mutate -replace ',', '+')" }
$joinAddress = '127.0.0.1'
if ($Steam) {
    # Both copies find each other through a real Steam lobby tagged with this run. They share one Steam account,
    # which cannot P2P to itself, so the data path is loopback TCP named in the lobby; P2P relay needs two accounts.
    $tag = 'lab' + (Get-Random -Maximum 1000000000)
    $hostArgs = "-rt-steam -rt-steam-lab $tag" + $hostArgs
    $clientArgs = "-rt-join-steam -rt-steam-lab $tag" + $clientArgs
    $spec.Expect = @($spec.Expect) + @('steam.lobby@host', 'steam.found@c1')
    $name += '-steam'
}
$out = Join-Path $modRoot "artifacts/tests/$name-results.json"
# Other mods' labs share this PC; under that load a copy now and then hangs before its first scene (its Unity log
# stops after "Input initialized", before the mod loads). Such a start is retried once, and the output says so.
$labDirs = @('lab-host', 'lab-c1', 'lab-c2', 'lab-c3')[0..($Players - 1)]
for ($attempt = 1; $attempt -le 2; $attempt++) {
    & (Join-Path $PSScriptRoot 'launch-lab.ps1') -Players $Players -HostArgs $hostArgs -ClientArgs $clientArgs -Tour $Tour -JoinAddress $joinAddress
    $started = $false
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep 1
        if (@($labDirs | Where-Object { -not (Test-Path (Join-Path $labRoot "$_/rick-and-morty-together-lab.log")) }).Count -eq 0) { $started = $true; break }
    }
    if ($started -or $attempt -eq 2) { break }
    Write-Output 'a lab copy hung at startup (no mod log after 90 s): restarting the copies once'
    foreach ($id in (Get-Content (Join-Path $labRoot 'lab-pids.txt') | ForEach-Object { [int]$_ })) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue; Wait-Process -Id $id -Timeout 15 -ErrorAction SilentlyContinue }
    foreach ($d in $labDirs) { Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $labRoot "$d/rick-and-morty-together-lab.log") }
    Start-Sleep 3
}
$deadline = (Get-Date).AddSeconds($Seconds)
$probe = Join-Path $labRoot 'tour-probe.json'
$probeArgs = @((Join-Path $modRoot 'tests/verify-lab.py'), $modRoot, '--out', $probe)
foreach ($e in $spec.Expect) { $probeArgs += @('--expect', $e) }
do {
    Start-Sleep 5
    python -I @probeArgs | Out-Null
    $probeResult = Get-Content $probe -Raw | ConvertFrom-Json
    $waiting = @($probeResult.failures | Where-Object { $_ -match 'not seen|no client passed' }).Count
    $failedCheck = @($probeResult.failures | Where-Object { $_ -match 'check .* failed' }).Count
    $pids = Get-Content (Join-Path $labRoot 'lab-pids.txt') | ForEach-Object { [int]$_ }
    $alive = @($pids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }).Count
} while (($waiting -gt 0) -and ($failedCheck -eq 0) -and ($alive -eq $pids.Count) -and ((Get-Date) -lt $deadline))
# A few more seconds so error, memory and state samples cover the end of the tour.
Start-Sleep 6
foreach ($id in $pids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
foreach ($id in $pids) { Wait-Process -Id $id -Timeout 15 -ErrorAction SilentlyContinue }
if ($alive -lt $pids.Count) { Write-Output 'a lab copy exited during the tour' }
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$verify = @((Join-Path $modRoot 'tests/verify-lab.py'), $modRoot, '--out', $out)
foreach ($e in $spec.Expect) { $verify += @('--expect', $e) }
python -I @verify | Out-Null
# Keep each tour's per-instance evidence; the next tour reuses the lab folders.
$keep = Join-Path $modRoot "artifacts/tests/logs/$name"
New-Item -ItemType Directory -Force $keep | Out-Null
foreach ($lab in 'lab-host', 'lab-c1', 'lab-c2', 'lab-c3') {
    $src = Join-Path $labRoot "$lab/rick-and-morty-together-lab.log"
    if (Test-Path $src) { Copy-Item -Force $src (Join-Path $keep "$lab.log") }
}
$result = Get-Content $out -Raw | ConvertFrom-Json
Write-Output ("{0}: {1}" -f $name, $(if ($result.pass) { 'PASS' } else { 'FAIL' }))
foreach ($f in $result.failures) { Write-Output "  - $f" }
if ($result.desync.npc.midjoin) { Write-Output ("  NPC lines: not compared for a mid-scene joiner: " + ($result.desync.npc.midjoin -join ', ')) }
if ($result.desync.npc.note) { Write-Output ("  NPC lines: " + $result.desync.npc.note) }
Write-Output ("  desync: rest max {0} m, moving max {1} m over {2} pairs" -f $result.desync.rest_max, $result.desync.moving_max, $result.desync.pairs)
foreach ($p in $result.instances.PSObject.Properties) {
    $i = $p.Value
    $passed = @($i.checks | Where-Object pass).Count; $total = @($i.checks).Count
    if ($i.modms_note) { Write-Output ("  {0}: frame cost {1}" -f $p.Name, $i.modms_note) }
    Write-Output ("  {0}: checks {1}/{2}, errors {3}, heap {4} MB (slope {5}), mod ms median {6} max {7}, fps {8}, mod objects {9}/{10}" -f $p.Name, $passed, $total, $i.errors_end, $i.heap_end_mb, $i.heap_slope_mb_per_sample, $i.modms_median, $i.modms_max, $i.fps_median, $i.modobjects_max, $i.modobjects_bound)
}
if (!$result.pass) { exit 1 }
