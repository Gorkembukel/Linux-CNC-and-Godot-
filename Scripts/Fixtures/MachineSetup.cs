using Godot;
using CncTrainer.Bridge;
using CncTrainer.Interaction;
using CncTrainer.Spec;

namespace CncTrainer.Fixtures;

/// <summary>What every machine's hands-on setup shares: the setup sheet on
/// the HUD, and the accidents that end in a stopped machine. Child of the
/// machine root.</summary>
public abstract partial class MachineSetup : Node3D, ISetupSheet
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

    /// <summary>Hangs the door in the enclosure's open front.</summary>
    protected void AddDoor(Node3D body, (Vector3 Centre, Vector2 Size)? opening)
    {
        if (opening is not var (centre, size))
            return;
        Door = MachineDoor.Create(centre, size);
        Door.Locked = () => Spec.DoorInterlock && Running;
        body.AddChild(Door);
    }

    /// <summary>Once per frame: a spindle or a program started with the
    /// door open stops the machine.</summary>
    protected void CheckDoorInterlock()
    {
        bool violation = Spec.DoorInterlock && Door != null && Door.IsOpen && Running;
        if (violation && !_ranWithDoorOpen)
            Safety.Trip(IncidentKind.Interlock, "KAPI AÇIK: makine kapı açıkken çalıştırıldı (önce kapıyı kapat)");
        _ranWithDoorOpen = violation;
    }

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
