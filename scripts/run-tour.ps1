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
}
if (!$tours.ContainsKey($Tour)) { throw "Unknown tour $Tour" }
$labRoot = Join-Path $modRoot 'private'
# Only copies under private/ (lab-owned, never the real game) are touched.
$busy = Get-CimInstance Win32_Process -Filter "Name='RickAndMortyVR.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($labRoot, [StringComparison]::OrdinalIgnoreCase) }
$busy | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; Wait-Process -Id $_.ProcessId -Timeout 15 -ErrorAction SilentlyContinue }
$spec = $tours[$Tour]
if ($Seconds -le 0) { $Seconds = $spec.Seconds }
foreach ($old in @(Get-ChildItem $labRoot -Filter 'rick-and-morty-together-lab.log' -Recurse -Depth 1 -ErrorAction SilentlyContinue)) {
    for ($try = 0; $try -lt 30; $try++) {
        try { Remove-Item -LiteralPath $old.FullName -Force -ErrorAction Stop; break }
        catch { if ($try -eq 29) { throw "The lab log $($old.FullName) is still in use after 30 s." }; Start-Sleep 1 }
    }
}
$hostArgs = ''; $clientArgs = ''
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
& (Join-Path $PSScriptRoot 'launch-lab.ps1') -Players $Players -HostArgs $hostArgs -ClientArgs $clientArgs -Tour $Tour -JoinAddress $joinAddress
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
Write-Output ("  desync: rest max {0} m, moving max {1} m over {2} pairs" -f $result.desync.rest_max, $result.desync.moving_max, $result.desync.pairs)
foreach ($p in $result.instances.PSObject.Properties) {
    $i = $p.Value
    $passed = @($i.checks | Where-Object pass).Count; $total = @($i.checks).Count
    Write-Output ("  {0}: checks {1}/{2}, errors {3}, heap {4} MB (slope {5}), mod ms median {6} max {7}, fps {8}, mod objects {9}/{10}" -f $p.Name, $passed, $total, $i.errors_end, $i.heap_end_mb, $i.heap_slope_mb_per_sample, $i.modms_median, $i.modms_max, $i.fps_median, $i.modobjects_max, $i.modobjects_bound)
}
if (!$result.pass) { exit 1 }
