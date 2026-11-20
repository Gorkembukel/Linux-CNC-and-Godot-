using System.Linq;
using Godot;
using CncTrainer.Fixtures;
using GDict = Godot.Collections.Dictionary;
using GArray = Godot.Collections.Array;

namespace CncTrainer.Save;

/// <summary>Turns the things a save holds into Godot dictionaries and back:
/// resources by their exported values, chuck and jaw states field by field.</summary>
public static class SaveCodec
{
    private const string ScriptKey = "$script";

    /// <summary>An optional dictionary entry: null when nothing was saved there.</summary>
    public static GDict Dict(Variant value) => value.VariantType == Variant.Type.Dictionary ? value.AsGodotDictionary() : null;

    /// <summary>A resource's exported values (nested resources too).</summary>
    public static GDict Resource(Resource resource)
    {
        var data = new GDict { [ScriptKey] = (resource.GetScript().As<Script>())?.ResourcePath ?? "" };
        foreach (var property in resource.GetPropertyList())
        {
            var usage = (PropertyUsageFlags)property["usage"].AsInt64();
            if (!usage.HasFlag(PropertyUsageFlags.ScriptVariable) || !usage.HasFlag(PropertyUsageFlags.Storage))
                continue;
            string name = property["name"].AsString();
            var value = resource.Get(name);
            data[name] = value.Obj is Resource nested ? Resource(nested) : value;
        }
        return data;
    }

    public static T Resource<T>(GDict data) where T : Resource => (T)ResourceObject(data);

    private static Resource ResourceObject(GDict data)
    {
        var script = GD.Load<CSharpScript>(data[ScriptKey].AsString());
        var resource = (Resource)script.New().AsGodotObject();
        foreach (var (key, value) in data)
        {
            string name = key.AsString();
            if (name != ScriptKey)
                resource.Set(name, value.Obj is GDict nested && nested.ContainsKey(ScriptKey) ? ResourceObject(nested) : value);
        }
        return resource;
    }

    public static GDict Voxels(Material.VoxelStock v) => new()
    {
        ["step"] = v.StepMm, ["counts"] = new Vector3I(v.CountX, v.CountY, v.CountZ), ["origin"] = v.Origin, ["distance"] = v.Distances(),
    };

    public static Material.VoxelStock Voxels(GDict d)
    {
        var counts = d["counts"].AsVector3I();
        return Material.VoxelStock.FromDistances(d["step"].AsSingle(), counts.X, counts.Y, counts.Z, d["origin"].AsVector3(), d["distance"].AsFloat32Array());
    }

    public static GDict Control(Spec.ControlMemory m) => new()
    {
        ["g5x_index"] = m.G5xIndex, ["g5x"] = m.G5x,
        ["tools"] = new GArray(m.Tools.Select(t => (Variant)new Vector3(t.Key, t.Value.X, t.Value.Z))),
    };

    public static Spec.ControlMemory Control(GDict d) => new(d["g5x_index"].AsInt32(), d["g5x"].AsFloat32Array(),
        d["tools"].AsGodotArray().Select(v => v.AsVector3()).ToDictionary(v => (int)v.X, v => (v.Y, v.Z)));

    public static GDict Dims(ChuckDims d) => new()
    {
        ["kind"] = (int)d.Kind, ["jaws"] = d.JawCount, ["opening"] = d.MaxOpeningMm, ["depth"] = d.JawDepthMm,
        ["bore"] = d.BoreMm, ["bore_depth"] = d.BoreDepthMm, ["pitch"] = d.ScrollPitchMm, ["family"] = d.JawFamily,
    };

    public static ChuckDims Dims(GDict d) => new((ChuckKind)d["kind"].AsInt32(), d["jaws"].AsInt32(), d["opening"].AsSingle(),
        d["depth"].AsSingle(), d["bore"].AsSingle(), d["bore_depth"].AsSingle(), d["pitch"].AsSingle(), d["family"].AsString());

    /// <summary>A whole chuck: its sizes and everything it is doing.</summary>
    public static GDict Chuck(ChuckGrip grip) => new() { ["dims"] = Dims(grip.Dims), ["state"] = Grip(grip.Snapshot()) };

    public static ChuckGrip Chuck(GDict d)
    {
        var grip = new ChuckGrip(Dims(d["dims"].AsGodotDictionary()), null);
        grip.Restore(Grip(d["state"].AsGodotDictionary()));
        return grip;
    }

    public static GDict Jaws(JawsState s) => new()
    {
        ["notch"] = s.Notch,
        ["slots"] = JawArray(s.Slots),
        ["loose"] = JawArray(s.Loose),
        ["collet"] = s.ColletMm is float c ? c : -1f,
        ["released"] = s.LastReleasedSlot,
    };

    public static JawsState Jaws(GDict d)
    {
        float collet = d["collet"].AsSingle();
        return new JawsState(d["notch"].AsInt32(), JawArray(d["slots"].AsGodotArray()), JawArray(d["loose"].AsGodotArray()),
            collet < 0f ? null : collet, d["released"].AsInt32());
    }

    private static GArray JawArray(SeatedJaw?[] jaws) => new(jaws.Select(j => j is SeatedJaw jaw
        ? (Variant)new GDict { ["kind"] = (int)jaw.Kind, ["number"] = jaw.Number, ["turn"] = jaw.Turn, ["caught"] = jaw.CaughtNotch }
        : default));

    private static SeatedJaw?[] JawArray(GArray jaws) => jaws.Select(v => v.Obj is GDict j
        ? new SeatedJaw((JawKind)j["kind"].AsInt32(), j["number"].AsInt32(), j["turn"].AsInt32(), j["caught"].AsInt32())
        : (SeatedJaw?)null).ToArray();

    public static GDict Grip(GripState s) => new()
    {
        ["jaws"] = Jaws(s.Jaws),
        ["opening"] = s.OpeningMm, ["torque"] = s.Torque, ["key"] = s.KeyIn,
        ["bar"] = s.HasBar, ["bar_d"] = s.BarDiameterMm, ["bar_l"] = s.BarLengthMm, ["stick_out"] = s.StickOutMm,
        ["soft_bored"] = s.SoftBoredForMm ?? -1f,
        ["inside"] = s.Inside, ["bore"] = s.BoreMm, ["bore_depth"] = s.BoreDepthMm,
        ["offset"] = new Vector2(s.OffsetX, s.OffsetY), ["tilt"] = new Vector2(s.TiltX, s.TiltY),
    };

    public static GripState Grip(GDict d)
    {
        float soft = d["soft_bored"].AsSingle();
        var offset = d["offset"].AsVector2();
        var tilt = d["tilt"].AsVector2();
        return new GripState(Jaws(d["jaws"].AsGodotDictionary()), d["opening"].AsSingle(), d["torque"].AsInt32(), d["key"].AsBool(),
            d["bar"].AsBool(), d["bar_d"].AsSingle(), d["bar_l"].AsSingle(), d["stick_out"].AsSingle(), soft < 0f ? null : soft,
            d["inside"].AsBool(), d["bore"].AsSingle(), d["bore_depth"].AsSingle(), offset.X, offset.Y, tilt.X, tilt.Y);
    }
}
