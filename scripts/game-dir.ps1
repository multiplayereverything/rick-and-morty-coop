# Finds the Rick and Morty: Virtual Rick-ality install: -GameDir, then $env:RAM_GAME, then Steam's libraries.
function Find-GameDir([string]$GameDir) {
    $candidates = @($GameDir, $env:RAM_GAME)
    $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $libraries = @($steam)
        $vdf = Join-Path $steam 'steamapps/libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) { $libraries += Select-String -LiteralPath $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' } }
        $candidates += $libraries | ForEach-Object { Join-Path $_ 'steamapps/common/RickAndMortyVR' }
    }
    $found = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'RickAndMortyVR_Data/Managed/Assembly-CSharp.dll')) } | Select-Object -First 1
    if (!$found) { throw 'The game folder was not found. Pass -GameDir or set $env:RAM_GAME.' }
    return (Resolve-Path -LiteralPath $found).Path
}
