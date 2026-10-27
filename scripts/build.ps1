# Builds the BepInEx plugin against the game's own .NET 3.5 assemblies (Unity 5.4.3, Mono).
param([string]$OutDir, [string]$GameDir)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'game-dir.ps1')
$gameRoot = Find-GameDir $GameDir
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler is required.' }
$managed = Join-Path $gameRoot 'RickAndMortyVR_Data/Managed'
$sources = @(Get-ChildItem (Join-Path $modRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
# The game runs the .NET 3.5 profile: compile against its mscorlib/System/System.Core, not the desktop framework.
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'UnityEngine.dll', 'UnityEngine.UI.dll', 'Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll') | ForEach-Object { Join-Path $managed $_ }
$core = Join-Path $modRoot 'deps/BepInEx-5.4.23.5/BepInEx/core'
$references += @((Join-Path $core 'BepInEx.dll'), (Join-Path $core '0Harmony.dll'))
foreach ($reference in $references) { if (!(Test-Path -LiteralPath $reference)) { throw "Missing reference: $reference" } }
$destination = if ($OutDir) { $OutDir } else { Join-Path $modRoot 'artifacts/BepInEx' }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$arguments = @('/nologo', '/nostdlib+', '/noconfig', '/target:library', '/optimize+', '/langversion:5', '/nowarn:1701,1702', "/out:$destination/RickAndMortyTogether.dll")
$arguments += ($references | ForEach-Object { "/reference:$_" })
$output = & $compiler @arguments @sources; $output | Where-Object { & $compiler @arguments @sources -match ': (error|warning) ' } | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built -> $destination/RickAndMortyTogether.dll"
