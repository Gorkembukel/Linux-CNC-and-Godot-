using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer.Machining;

/// <summary>
/// A tool the player carries to the machine: a cutter in its holder, or a
/// lathe tool on its block. No two are set up alike: a mill cutter sticks
/// out of its holder a few millimetres more or less than nominal, a lathe
/// insert sits a few tenths off, and the control knows none of it until the
/// tool is measured and its offset entered. Local frame: tip at the origin.
/// </summary>
public partial class ToolItem : Carryable
{
    public ToolSpec Spec { get; private set; }
    public ToolState State { get; } = new();
    /// <summary>See MountedTool.ErrorMm.</summary>
    public Vector3 ErrorMm;

    public float TrueLengthMm => Spec.LengthMm + ErrorMm.Y;
    public MountedTool Mounted => new(Spec, State, ErrorMm);

    public string Label => $"T{Spec.Number} {Spec.Description}" + (State.Broken ? " (KIRIK)" : "");

    /// <summary>exact = a tool with no setting error, for tests.</summary>
    public static ToolItem Create(ToolSpec spec, bool exact = false)
    {
        var item = new ToolItem { Spec = spec, Name = $"ToolT{spec.Number}", Mass = 0.8f };
        item.DisplayName = item.Label;
        if (!exact)
        {
            static float Off(float min, float max) => (float)GD.RandRange(min, max) * (GD.Randf() < 0.5f ? -1f : 1f);
            item.ErrorMm = spec.IsLatheTool ? new Vector3(Off(0.08f, 0.3f), 0f, Off(0.1f, 0.4f)) : new Vector3(0f, Off(0.5f, 3f), 0f);
        }

        Vector3 size;
        if (spec.IsLatheTool)
        {
            item.AddChild(ToolMeshBuilder.LatheTool(spec, 0.05f));
            size = new Vector3(0.05f, 0.06f, 0.03f);
        }
        else
        {
            item.AddChild(ToolMeshBuilder.MillTool(spec));
            size = new Vector3(0.06f, spec.LengthMm * WorldScale.MmToM + ToolMeshBuilder.HolderLength, 0.06f);
        }
        item.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0f, size.Y / 2f, 0f) });
        return item;
    }
}
