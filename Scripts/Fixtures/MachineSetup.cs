using Godot;
using CncTrainer.Bridge;
using CncTrainer.Interaction;
using CncTrainer.Spec;

namespace CncTrainer.Fixtures;

/// <summary>What every machine's hands-on setup shares: the setup sheet on
/// the HUD, and the accidents that end in a stopped machine. Child of the
/// machine root.</summary>
public abstract partial class MachineSetup : Node3D, ISetupSheet, Save.ISaveable
{
    protected MachineSpec Spec { get; private set; }
    protected BridgeClient Bridge { get; private set; }
    protected MachineSafety Safety { get; private set; }
    /// <summary>Enclosed machines only.</summary>
    public MachineDoor Door { get; private set; }

    private bool _ranWithDoorOpen;

    protected void Init(MachineSpec spec, BridgeClient bridge, MachineSafety safety)
    {
        Spec = spec;
        Bridge = bridge;
        Safety = safety;
        AddToGroup("setup_sheets");
    }

    private bool Running => System.Math.Abs(Bridge.State.SpindleRpm) > 1.0
                            || (Bridge.State.InCycle && Bridge.State.TaskMode == TaskMode.Auto);

    /// <summary>Fits the machine's door or guard (null: the machine has neither).</summary>
    protected void AddDoor(Node3D parent, MachineDoor door)
    {
        if (door == null)
            return;
        Door = door;
        Door.Locked = () => Interlocked && Running;
        parent.AddChild(Door);
    }

    private bool Interlocked => Spec.DoorInterlock && Workshop.WorkshopSettings.DoorInterlocks;

    /// <summary>Once per frame: a spindle or a program started with the
    /// door open stops the machine.</summary>
    protected void CheckDoorInterlock()
    {
        bool open = Door != null && Door.IsOpen && Running;
        if (open && Interlocked && !_ranWithDoorOpen)
            Safety.Trip(IncidentKind.Interlock, $"{Door.Noun.ToUpper(TurkishCulture)} AÇIK: makine {Door.Noun.ToLower(TurkishCulture)} açıkken çalıştırıldı (önce kapat)");
        _ranWithDoorOpen = open && Interlocked;
        // Without an interlock nothing stops it: the chips come out at the operator.
        _openRunSeconds = open && !Interlocked ? _openRunSeconds + GetProcessDeltaTime() : 0.0;
        if (_openRunSeconds >= OpenRunRecordSeconds)
        {
            _openRunSeconds = -60.0;
            Safety.Record(IncidentKind.Unguarded, $"{Door.Noun.ToUpper(TurkishCulture)} AÇIK ÇALIŞILDI: talaş ve soğutma sıvısı operatöre sıçradı");
        }
    }

    private static readonly System.Globalization.CultureInfo TurkishCulture = new("tr-TR");
    private const double OpenRunRecordSeconds = 2.0;
    private double _openRunSeconds;

    public Godot.Collections.Dictionary Save()
    {
        var data = SaveSetup();
        data["door"] = Door?.IsOpen ?? true;
        return data;
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        Door?.SetOpen(data["door"].AsBool());
        LoadSetup(data);
    }

    protected abstract Godot.Collections.Dictionary SaveSetup();
    protected abstract void LoadSetup(Godot.Collections.Dictionary data);

    public virtual Vector3 SheetPosition => GlobalPosition;
    public string SheetText => $"AYAR FÖYÜ - {Spec.MachineName}\n" + SheetBody
        + (Door != null ? Sheet.Mark(!Door.IsOpen) + "Kapı kapalı\n" : "");
    protected abstract string SheetBody { get; }

    /// <summary>The part leaves the machine the hard way.</summary>
    protected void Throw(RigidBody3D part, string reason)
    {
        part.Freeze = false;
        part.ApplyCentralImpulse(GlobalTransform.Basis * new Vector3(0.6f, 1.2f, 1.4f) * part.Mass * 2.5f);
        Safety.Trip(IncidentKind.PartThrown, reason);
    }

    /// <summary>A chuck key still in the chuck when it starts to turn.</summary>
    protected void CheckKeyLeftIn(JawChuck chuck, bool turning, string reason)
    {
        if (!chuck.Grip.KeyIn || !turning)
            return;
        var key = chuck.TakeKey();
        key.ApplyCentralImpulse(GlobalTransform.Basis * new Vector3(0.3f, 1.5f, 2f) * key.Mass * 3f);
        Safety.Trip(IncidentKind.KeyLeftIn, reason);
    }
}
