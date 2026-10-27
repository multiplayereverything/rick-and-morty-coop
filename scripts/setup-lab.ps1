# Creates up to four isolated lab copies of the game under private/: the exe and steam_api64.dll are hardlinked, the
# Data folder is a junction (never edit game bytes through it), BepInEx is copied in, and steam_appid.txt exists only
# here so a direct launch does not restart through Steam. The real install is never touched.
param([int]$Copies = 4, [string]$GameDir)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'game-dir.ps1')
$game = Find-GameDir $GameDir
$bep = Join-Path $modRoot 'deps/BepInEx-5.4.23.5'
if (!(Test-Path (Join-Path $bep 'winhttp.dll'))) { throw "BepInEx 5.4.23.5 (x64) is expected in $bep" }
$names = @('lab-host', 'lab-c1', 'lab-c2', 'lab-c3') | Select-Object -First $Copies
foreach ($name in $names) {
    $dir = Join-Path $modRoot "private/$name"
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($file in 'RickAndMortyVR.exe', 'steam_api64.dll') {
        $target = Join-Path $dir $file
        if (!(Test-Path $target)) { New-Item -ItemType HardLink -Path $target -Target (Join-Path $game $file) | Out-Null }
    }
    $data = Join-Path $dir 'RickAndMortyVR_Data'
    if (!(Test-Path $data)) { New-Item -ItemType Junction -Path $data -Target (Join-Path $game 'RickAndMortyVR_Data') | Out-Null }
    # Only what is missing: a rerun must not trip over a loader file a running copy has open.
    foreach ($file in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
        if (!(Test-Path (Join-Path $dir $file))) { Copy-Item (Join-Path $bep $file) $dir }
    }
    New-Item -ItemType Directory -Force (Join-Path $dir 'BepInEx/core'), (Join-Path $dir 'BepInEx/plugins/RickAndMortyTogether'), (Join-Path $dir 'BepInEx/config') | Out-Null
    Copy-Item -Force (Join-Path $bep 'BepInEx/core/*') (Join-Path $dir 'BepInEx/core')
    # The release's config: entrypoint at the first scene load, no console window (it would take focus).
    Copy-Item -Force (Join-Path $modRoot 'config/BepInEx.cfg') (Join-Path $dir 'BepInEx/config/BepInEx.cfg')
    Set-Content (Join-Path $dir 'steam_appid.txt') '469610' -NoNewline
    Write-Output "ready: $dir"
}
