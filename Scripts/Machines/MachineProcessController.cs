using Godot;

namespace CncTrainer.Bridge;

/// <summary>
/// Starts/stops one machine's backing WSL/LinuxCNC/bridge process,
/// parameterized entirely from its MachineProfile -- this is what lets
/// Godot itself bring the machine up and tear it down, instead of a human
/// manually running a bridge process in its own terminal window before
/// launching the game and closing it by hand afterward (the old
/// `start.ps1` flow). Stateless/static, same style as
/// WorkpiecePartBuilder: no instance to construct, just two entry points.
/// </summary>
public static class MachineProcessController
{
    /// <summary>Cleans stale IPC (synchronous, fast -- same step `start.ps1`
    /// used to run first) then launches `bridge/main.py` for this profile,
    /// fire-and-forget. Not awaited: BridgeClient already tolerates "not
    /// connected yet" and reconnects the moment packets start (brief
    /// section 2's existing design), so there's no readiness handshake to
    /// build here. stdout/stderr redirected to a log file by the remote
    /// shell itself (not via .NET stream redirection, which would need
    /// draining to avoid blocking) -- replaces the visible PowerShell
    /// window `start.ps1` used to open.</summary>
    public static void StartBackingProcess(MachineProfile profile)
    {
        string wslRoot = GetWslProjectPath();
        try
        {
            RunWsl(profile.WslDistro, $"bash {wslRoot}/bridge/clear_stale_shm.sh", waitMs: 10000);

            string logPath = $"{wslRoot}/bridge/bridge_session_{profile.Name}.log";
            string startCmd = $"python3 -u {wslRoot}/bridge/main.py --ini {profile.IniFile} --rate-hz {profile.RateHz} " +
                               $"--state-port {profile.StatePort} --command-port {profile.CommandPort} " +
                               $"> {logPath} 2>&1";
            StartWslFireAndForget(profile.WslDistro, startCmd);
            GD.Print($"[MachineProcessController] started '{profile.Name}' bridge on distro '{profile.WslDistro}' " +
                      $"(state={profile.StatePort} command={profile.CommandPort}); log: {logPath}");
        }
        catch (System.Exception e)
        {
            // Never block/crash the game over a process-launch failure --
            // same philosophy BridgeClient itself follows for a down bridge.
            GD.PrintErr($"[MachineProcessController] failed to start '{profile.Name}': {e.Message}");
        }
    }

    /// <summary>Sends SIGTERM to bridge/main.py inside this profile's
    /// distro, then unconditionally sweeps any straggler LinuxCNC processes
    /// and stale SysV IPC behind it. Two steps, not one: SIGTERM alone
    /// isn't reliably enough -- found live, the hard way. `wsl.exe -d
    /// <distro> -- bash -lc "..."` launches `bash` as the direct child,
    /// which then forks `python3` as ITS child; `pkill -f bridge/main.py`
    /// matches both (pkill -f matches the whole command line, and bash's
    /// own cmdline contains the inner python invocation text too), and
    /// when the outer bash dies, WSL's own invocation teardown can cut the
    /// deeply-nested grandchild chain (python -> linuxcnc wrapper ->
    /// linuxcncsvr/rtapi_app) short mid-shutdown, leaving linuxcncsvr or
    /// rtapi_app orphaned and still running even though bridge/main.py's
    /// own clean-shutdown code (confirmed correct by reading it) never got
    /// the chance to finish. The same unconditional sweep
    /// `LinuxCNCProcess._kill_stragglers`/`clear_stale_shm.sh` already do
    /// for exactly this situation is reused here as the guarantee, instead
    /// of trying to fix the exact WSL process-teardown race.</summary>
    public static void StopBackingProcess(MachineProfile profile)
    {
        try
        {
            RunWsl(profile.WslDistro, "pkill -SIGTERM -f '[b]ridge/main.py'", waitMs: 5000);
            RunWsl(profile.WslDistro,
                "pkill -9 -x linuxcncsvr; pkill -9 -x milltask; pkill -9 -x rtapi_app; " +
                "pkill -9 -f '[b]ridge/main.py'; true",
                waitMs: 5000);
            string wslRoot = GetWslProjectPath();
            RunWsl(profile.WslDistro, $"bash {wslRoot}/bridge/clear_stale_shm.sh", waitMs: 10000);
            GD.Print($"[MachineProcessController] stopped '{profile.Name}' bridge on distro '{profile.WslDistro}'");
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[MachineProcessController] failed to stop '{profile.Name}': {e.Message}");
        }
    }

    private static void RunWsl(string distro, string shellCommand, int waitMs)
    {
        var psi = BuildWslStartInfo(distro, shellCommand);
        using var p = System.Diagnostics.Process.Start(psi);
        p?.WaitForExit(waitMs);
    }

    private static void StartWslFireAndForget(string distro, string shellCommand)
    {
        var psi = BuildWslStartInfo(distro, shellCommand);
        System.Diagnostics.Process.Start(psi);
    }

    private static System.Diagnostics.ProcessStartInfo BuildWslStartInfo(string distro, string shellCommand)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(distro);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-lc");
        psi.ArgumentList.Add(shellCommand); // one argv entry; bash -lc parses it as a whole command string
        return psi;
    }

    /// <summary>Godot's res://project root, as the /mnt/&lt;drive&gt;/... path
    /// WSL sees it at -- same conversion start.ps1 used to do by hand.</summary>
    private static string GetWslProjectPath()
    {
        string winPath = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        string driveLetter = winPath.Substring(0, 1).ToLowerInvariant();
        string rest = winPath.Substring(2).Replace('\\', '/');
        return $"/mnt/{driveLetter}{rest}";
    }
}
