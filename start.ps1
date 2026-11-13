<#
Starts the whole stack with one command: cleans stale LinuxCNC/IPC state,
starts the bridge inside WSL2 (in its own window, so you can watch its log and
Ctrl+C it independently), waits for it to report ready, then launches Godot.

Usage:  .\start.ps1
Requires the one-time setup in README.md to already be done (WSL2 +
linuxcnc-uspace installed, the Windows Firewall rule for UDP 9200/9201 added,
and $GodotExe below pointed at your actual Godot install).
#>

$GodotExe = "E:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
$ProjectPath = $PSScriptRoot

if (-not (Test-Path $GodotExe)) {
    Write-Host "Godot executable not found at $GodotExe -- edit `$GodotExe at the top of start.ps1 to point at your install." -ForegroundColor Red
    exit 1
}

# e.g. "C:\Users\gorke\Documents\linux" -> "/mnt/c/Users/gorke/Documents/linux"
$driveLetter = $ProjectPath.Substring(0, 1).ToLower()
$wslProjectPath = "/mnt/$driveLetter" + $ProjectPath.Substring(2).Replace('\', '/')

Write-Host "Cleaning up any stale LinuxCNC/IPC state in WSL..." -ForegroundColor Cyan
wsl -d Ubuntu -- bash "$wslProjectPath/bridge/clear_stale_shm.sh" | Out-Null

Write-Host "Starting the bridge in a new window..." -ForegroundColor Cyan
$bridgeWindow = Start-Process powershell -ArgumentList "-NoExit", "-Command", "wsl -d Ubuntu -- python3 -u $wslProjectPath/bridge/main.py --rate-hz 100" -PassThru

Write-Host "Waiting for the bridge to report ready (this can take up to ~90s on a cold LinuxCNC start)..." -ForegroundColor Cyan
$endpointFile = Join-Path $ProjectPath "bridge\.bridge_endpoint"
$deadline = (Get-Date).AddSeconds(120)
while (-not (Test-Path $endpointFile) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 1
}
if (-not (Test-Path $endpointFile)) {
    Write-Host "Bridge did not become ready in time -- check its window for errors." -ForegroundColor Red
    exit 1
}
Start-Sleep -Seconds 2 # give LinuxCNC a moment past endpoint-file-write to finish settling

Write-Host "Launching Godot..." -ForegroundColor Cyan
& $GodotExe --path $ProjectPath

Write-Host "Godot closed. The bridge is still running in its own window -- close it manually (Ctrl+C) when done." -ForegroundColor Yellow
