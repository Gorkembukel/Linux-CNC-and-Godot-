using System.Collections.Generic;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Player;

namespace CncTrainer;

public enum IncidentKind { PartThrown, Crash, KeyLeftIn, ToolBroken, Overload, Interlock, UnsafeHands, NoGlasses, Unguarded }

public readonly record struct Incident(IncidentKind Kind, string Text, double AtSeconds);

/// <summary>
/// The one place a machine is stopped because the operator got something
/// wrong. Everything that can go wrong (workholding, tools, collisions,
/// interlocks) reports here; it stops the machine, tells the player why, and
/// keeps the list of what happened.
/// </summary>
public sealed class MachineSafety
{
    private readonly Node _owner;
    private readonly BridgeClient _bridge;
    private readonly List<Incident> _incidents = new();

    public IReadOnlyList<Incident> Incidents => _incidents;
    public event System.Action<Incident> Tripped;

    public MachineSafety(Node owner, BridgeClient bridge)
    {
        _owner = owner;
        _bridge = bridge;
    }

    /// <summary>Stops the program, the spindle and the drives. A crash
    /// also costs the machine its reference: it has to be homed again.</summary>
    public void Trip(IncidentKind kind, string text)
    {
        if (kind == IncidentKind.Crash)
            text += "\nReferans kayboldu: makineyi aç, HOME ALL";
        var incident = new Incident(kind, text, Time.GetTicksMsec() / 1000.0);
        _incidents.Add(incident);
        _bridge.StopProgram();
        _bridge.SpindleOff();
        _bridge.MachineOff();
        if (kind == IncidentKind.Crash)
            _bridge.Unhome();
        PlayerController.Notify(_owner, text);
        Tripped?.Invoke(incident);
    }

    /// <summary>Puts a safety slip on the record and tells the player,
    /// without stopping the machine.</summary>
    public void Record(IncidentKind kind, string text)
    {
        var incident = new Incident(kind, text, Time.GetTicksMsec() / 1000.0);
        _incidents.Add(incident);
        PlayerController.Notify(_owner, text);
        Tripped?.Invoke(incident);
    }

    /// <summary>Tells the player something without stopping the machine.</summary>
    public void Warn(string text) => PlayerController.Notify(_owner, text);
}
