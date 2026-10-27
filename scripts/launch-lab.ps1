# Starts up to four lab copies without a headset: minimized in the taskbar without activation, with a small
# (480x270) restore position parked at the right edge of the screen, stacked; BelowNormal priority, lowest quality,
# 30 fps. The user can click one open at any time; the script never re-minimizes, moves or focuses it again.
# No keys or mouse input ever go to the desktop.
# Test-owned PIDs go to private/lab-pids.txt; stop only those (never a user-owned game process).
param(
    [int]$Players = 2, [int]$Seconds = 0,
    [string]$HostArgs = '', [string]$ClientArgs = '', [string]$Tour = '', [string]$JoinAddress = '127.0.0.1'
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$labs = @(
    @{ Name = 'host'; Dir = 'lab-host' },
    @{ Name = 'c1'; Dir = 'lab-c1' },
    @{ Name = 'c2'; Dir = 'lab-c2' },
    @{ Name = 'c3'; Dir = 'lab-c3' }
) | Select-Object -First $Players

if (-not ('RamLab.Win' -as [type])) {
    Add-Type -Namespace RamLab -Name Win -MemberDefinition @'
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public System.IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
[StructLayout(LayoutKind.Sequential)]
public struct RECT { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; }
[StructLayout(LayoutKind.Sequential)]
public struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT minPos, maxPos; public RECT normalPos; }
[DllImport("user32.dll")] public static extern bool SetWindowPlacement(System.IntPtr hWnd, ref WINDOWPLACEMENT wp);
[StructLayout(LayoutKind.Sequential)]
public struct PROCESS_INFORMATION { public System.IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern bool CreateProcess(string app, string cmd, System.IntPtr pa, System.IntPtr ta, bool inherit, uint flags, System.IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
[DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int cmd);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
// Starts a process whose first window shows minimized without activation (SW_SHOWMINNOACTIVE), at below-normal priority.
public static int Start(string exe, string args, string dir) {
    STARTUPINFO si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO)); si.dwFlags = 1; si.wShowWindow = 7;
    PROCESS_INFORMATION pi;
    if (!CreateProcess(exe, "\"" + exe + "\" " + args, System.IntPtr.Zero, System.IntPtr.Zero, false, 0x4000, System.IntPtr.Zero, dir, ref si, out pi)) throw new System.ComponentModel.Win32Exception();
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess); return pi.dwProcessId;
}
// Minimized (not activated); restoring it puts it at the right edge of the primary screen, stacked from the top.
public static void Park(System.IntPtr hWnd, int slot) {
    int w = 496, h = 309, sw = GetSystemMetrics(0);
    WINDOWPLACEMENT wp = new WINDOWPLACEMENT(); wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
    wp.showCmd = 7; wp.normalPos.Left = sw - w; wp.normalPos.Top = slot * h; wp.normalPos.Right = sw; wp.normalPos.Bottom = slot * h + h;
    SetWindowPlacement(hWnd, ref wp);
}
'@
}

$logs = Join-Path $modRoot 'private/lab-logs'
New-Item -ItemType Directory -Force $logs | Out-Null
Get-ChildItem $logs -Filter '*.log' -ErrorAction SilentlyContinue | Remove-Item -Force
# -vrmode None: no OpenVR device, so no SteamVR and no headset; the mod's lab rig drives head and hands.
$window = '-vrmode None -screen-fullscreen 0 -screen-width 480 -screen-height 270 -screen-quality Fastest'
$started = @()
$slot = 0
foreach ($lab in $labs) {
    $dir = Join-Path $modRoot "private/$($lab.Dir)"
    Copy-Item -Force (Join-Path $modRoot 'artifacts/BepInEx/RickAndMortyTogether.dll') (Join-Path $dir 'BepInEx/plugins/RickAndMortyTogether/')
    $log = Join-Path $logs "$($lab.Name).log"
    # Lab port 27140 keeps tests clear of a real game hosting on 27040 at the same time.
    $role = if ($lab.Name -eq 'host') { "-rt-host -rt-port 27140 -rt-name Host -rt-expect $Players $HostArgs" } else { "-rt-join $JoinAddress -rt-port 27140 -rt-name $($lab.Name.ToUpper()) $ClientArgs" }
    if ($Tour) { $role += " -rt-tour $Tour" }
    $id = [RamLab.Win]::Start((Join-Path $dir 'RickAndMortyVR.exe'), "$window -logFile `"$log`" -rt-lab -rt-slot $slot $role", $dir)
    $p = Get-Process -Id $id
    $started += $p
    for ($i = 0; $i -lt 150; $i++) { $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { [RamLab.Win]::Park($p.MainWindowHandle, $slot); break }; Start-Sleep -Milliseconds 100 }
    $slot++
    if ($lab.Name -eq 'host') { Start-Sleep 4 }
}
Set-Content (Join-Path $modRoot 'private/lab-pids.txt') (($started | ForEach-Object Id) -join "`n")
Write-Output ("started " + (($started | ForEach-Object { $_.Id }) -join ', '))
if ($Seconds -gt 0) {
    Start-Sleep $Seconds
    foreach ($p in $started) { if (!$p.HasExited) { Stop-Process -Id $p.Id -Force } }
    foreach ($p in $started) { $p.WaitForExit(15000) | Out-Null }
    Write-Output 'stopped lab processes'
}
