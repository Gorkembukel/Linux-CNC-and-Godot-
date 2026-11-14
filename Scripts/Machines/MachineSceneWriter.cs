using Godot;
using CncTrainer.Spec;

namespace CncTrainer;

public static class MachineSceneWriter
{
    public const string MachinesDir = "res://Scenes/Machines";

    public static string ScenePath(MachineSpec spec) => $"{MachinesDir}/{spec.MachineName}.tscn";

    /// <summary>Saves a machine scene (root + its own copy of the spec +
    /// an editor-movable console) and writes its LinuxCNC config.</summary>
    public static Error Save(MachineSpec spec)
    {
        var ownSpec = (MachineSpec)spec.DuplicateDeep(Resource.DeepDuplicateMode.All);
        var root = new ProceduralMachine { Name = ownSpec.MachineName, Spec = ownSpec };
        var console = new ConsoleStand { Name = "ConsoleStand", Position = ConsolePosition(ownSpec) };
        root.AddChild(console);
        console.Owner = root;

        var packed = new PackedScene();
        var result = packed.Pack(root);
        root.Free();
        if (result != Error.Ok)
            return result;
        result = ResourceSaver.Save(packed, ScenePath(ownSpec));
        if (result == Error.Ok)
            ownSpec.WriteConfigFiles();
        return result;
    }

    /// <summary>Front-left of the machine, on the floor.</summary>
    private static Vector3 ConsolePosition(MachineSpec spec)
    {
        if (spec.Kind == MachineKind.Mill)
            return new Vector3(-(spec.Mill.BaseWidthMm / 2f + 350f), 0f, spec.Mill.TableSizeYMm / 2f + 300f) * WorldScale.MmToM;
        var l = spec.Lathe;
        return new Vector3(l.StockZMin - 50f - l.HeadstockLengthMm - 350f, 0f, l.BedDepthMm / 2f + 300f) * WorldScale.MmToM;
    }
}
