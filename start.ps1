<#
Launches the game. Godot itself now starts its machines' backing
LinuxCNC/bridge processes on its own (see Scripts/Machines/
MachineProcessController.cs) and stops them cleanly when you close the
window -- no separate bridge window or manual cleanup needed before or
after. First cold start of LinuxCNC can take up to ~90 seconds (normal, see
PLAN.md's Phase 0 notes); the on-screen status will just say "BRIDGE
DISCONNECTED" until it's up, then connect on its own.

Usage:  .\start.ps1
Requires the one-time setup in README.md to already be done (WSL2 +
linuxcnc-uspace installed, the Windows Firewall rule for UDP 9200/9201
added, and $GodotExe below pointed at your actual Godot install).
#>

$GodotExe = "E:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
$ProjectPath = $PSScriptRoot

if (-not (Test-Path $GodotExe)) {
    Write-Host "Godot executable not found at $GodotExe -- edit `$GodotExe at the top of start.ps1 to point at your install." -ForegroundColor Red
    exit 1
}

& $GodotExe --path $ProjectPath
