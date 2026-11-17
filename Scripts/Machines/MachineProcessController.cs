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
    /// <summary>Launches `bridge/main.py` for this profile inside its own
    /// namespaces (`bridge/run_isolated.sh`), fire-and-forget. Not awaited:
    /// BridgeClient already tolerates "not connected yet" and reconnects the
    /// moment packets start, so there's no readiness handshake to build
    /// here. stdout/stderr redirected to a log file by the remote shell
    /// itself (not via .NET stream redirection, which would need draining
    /// to avoid blocking).</summary>
    public static void StartBackingProcess(MachineProfile profile)
    {
        string wslRoot = GetWslProjectPath();
        try
        {
            // A previous game run that crashed leaves its machine running.
            KillMachine(profile);

            string user = ReadWsl(profile.WslDistro, "whoami", waitMs: 10000);
            if (user.Length == 0)
            {
                GD.PrintErr($"[MachineProcessController] '{profile.Name}' not started: WSL distro " +
                            $"'{profile.WslDistro}' did not answer.");
                return;
            }

            string logPath = $"{wslRoot}/bridge/bridge_session_{profile.Name}.log";
            // --command-port stays last: KillMachine finds the machine by it.
            string startCmd = $"bash {wslRoot}/bridge/run_isolated.sh {user} " +
                               $"python3 -u {wslRoot}/bridge/main.py --ini {profile.IniFile} --rate-hz {profile.RateHz} " +
                               $"--state-port {profile.StatePort} --command-port {profile.CommandPort} " +
                               $"> {logPath} 2>&1";
            System.Diagnostics.Process.Start(BuildWslStartInfo(profile.WslDistro, startCmd, asRoot: true));
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

    public static void StopBackingProcess(MachineProfile profile)
    {
        try
        {
            KillMachine(profile);
            GD.Print($"[MachineProcessController] stopped '{profile.Name}' bridge on distro '{profile.WslDistro}'");
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[MachineProcessController] failed to stop '{profile.Name}': {e.Message}");
        }
    }

    /// <summary>Kills the `unshare` that owns this machine's namespaces.
    /// The kernel then kills every process inside its PID namespace
    /// (bridge, linuxcncsvr, milltask, rtapi_app) and frees its IPC
    /// namespace and private /tmp, so nothing stale is left to sweep and no
    /// other machine in the distro is touched.</summary>
    private static void KillMachine(MachineProfile profile)
    {
        using var p = System.Diagnostics.Process.Start(BuildWslStartInfo(profile.WslDistro,
            $"pkill -9 -f '^unshare .*--command-port {profile.CommandPort}$'", asRoot: true));
        p?.WaitForExit(5000);
    }

    private static string ReadWsl(string distro, string shellCommand, int waitMs)
    {
        var psi = BuildWslStartInfo(distro, shellCommand, asRoot: false);
        psi.RedirectStandardOutput = true;
        using var p = System.Diagnostics.Process.Start(psi);
        if (p == null)
            return "";
        string output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(waitMs);
        return output;
    }

    private static System.Diagnostics.ProcessStartInfo BuildWslStartInfo(string distro, string shellCommand, bool asRoot)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(distro);
        if (asRoot)
        {
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add("root");
        }
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
