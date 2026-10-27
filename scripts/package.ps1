# Builds the release ZIP: BepInEx 5.4.23.5 (x64) with this mod's BepInEx.cfg, plus the plugin. No game files.
# Unzip into the game folder, next to RickAndMortyVR.exe.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$modRoot = Split-Path $PSScriptRoot -Parent
$version = (Select-String -Path (Join-Path $modRoot 'src/Runtime.cs') -Pattern 'ModVersion = "([^"]+)"').Matches[0].Groups[1].Value
$out = Join-Path $modRoot 'artifacts/release'
New-Item -ItemType Directory -Force $out | Out-Null
$stage = Join-Path $modRoot 'private/package-stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
Copy-Item -Recurse (Join-Path $modRoot 'deps/BepInEx-5.4.23.5') $stage
New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx/plugins/RickAndMortyTogether'), (Join-Path $stage 'BepInEx/config') | Out-Null
Copy-Item (Join-Path $modRoot 'artifacts/BepInEx/RickAndMortyTogether.dll') (Join-Path $stage 'BepInEx/plugins/RickAndMortyTogether/')
# Unity 5.4 needs BepInEx to start at the first scene load; this config sets that entrypoint.
Copy-Item (Join-Path $modRoot 'config/BepInEx.cfg') (Join-Path $stage 'BepInEx/config/BepInEx.cfg')
Copy-Item (Join-Path $modRoot 'README.md') (Join-Path $stage 'RickAndMortyTogether-README.md')
foreach ($bad in @(Get-ChildItem $stage -Recurse -File | Where-Object { $_.Name -match '^(Assembly-CSharp|UnityEngine|RickAndMortyVR)' -or $_.Extension -in '.assets', '.resS', '.resource' })) { throw "Game file in the package: $($bad.FullName)" }
$zip = Join-Path $out "RickAndMortyTogether-BepInEx-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
# Not Compress-Archive: Windows PowerShell 5 writes backslashes into entry names, which other unzip tools mangle.
$root = (Resolve-Path $stage).Path
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $name = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name)
    }
} finally { $archive.Dispose() }
Write-Output "Packaged $zip"
