using System.Linq;
using Godot;
using CncTrainer.Chips;
using CncTrainer.Spec;
using CncTrainer.Interaction;
using CncTrainer.Material;

namespace CncTrainer.Workshop;

/// <summary>A turned part off the lathe: keeps its radius profile so its
/// diameters can still be measured. Local frame: X along the axis, origin
/// at the centre of mass.</summary>
public partial class TurnedPart : Carryable, IMeasurable
{
    private float[] _radiiMm, _innerMm;
    private float _stepMm, _startMm;

    public MaterialKind Material;

    /// <summary>Builds the part for a run of radii (radiiMm[0] at machine
    /// Z = startZmm). Null when it is too short to be a part; centreZmm is
    /// where its origin belongs along the machine's Z.</summary>
    public static TurnedPart Build(float[] radiiMm, float stepMm, float startZmm, float densityKgPerM3, Godot.Material look, out float centreZmm,
        float[] innerMm = null)
    {
        centreZmm = startZmm;
        innerMm ??= new float[radiiMm.Length];
        if (WorkpiecePartBuilder.Build(radiiMm, stepMm, startZmm, densityKgPerM3, innerMm: innerMm) is not WorkpiecePartBuilder.BuiltPart built)
            return null;
        centreZmm = built.CentroidZmm;
        var part = new TurnedPart
        {
            Name = "TurnedPart",
            DisplayName = "Tornalanmış parça",
            Mass = Mathf.Max(0.01f, built.MassKg),
            _radiiMm = radiiMm,
            _innerMm = innerMm,
            _stepMm = stepMm,
            _startMm = startZmm - built.CentroidZmm,
        };
        part.AddChild(new MeshInstance3D { Mesh = built.Mesh, MaterialOverride = look });
        part.AddChild(new CollisionShape3D { Shape = built.Collision });
        return part;
    }

    public string Reading(HandToolKind gauge, Vector3 globalPoint)
    {
        var local = ToLocal(globalPoint) / WorldScale.MmToM;
        int index = Mathf.Clamp(Mathf.RoundToInt((local.X - _startMm) / _stepMm), 0, _radiiMm.Length - 1);
        return BoreOrOutside(gauge, new Vector2(local.Y, local.Z).Length(), _radiiMm[index], _innerMm[index]);
    }

    /// <summary>Radius of the end that goes into the jaws.</summary>
    public float EndRadiusMm(bool reversed) => reversed ? _radiiMm[^1] : _radiiMm[0];

    /// <summary>The part as stock for a chuck again: its radii from the end
    /// that goes into the jaws (reversed = the other end first).</summary>
    public StockItem ToStock(bool reversed)
    {
        var radii = (float[])_radiiMm.Clone();
        var inner = (float[])_innerMm.Clone();
        if (reversed)
        {
            System.Array.Reverse(radii);
            System.Array.Reverse(inner);
        }
        float diameter = 2f * Mathf.Max(radii.Max(), 0.1f);
        var item = StockItem.Create(new StockBlank(StockShape.CylinderAlongX,
            new Vector3((radii.Length - 1) * _stepMm, diameter, diameter), Material));
        item.DisplayName = DisplayName;
        item.Mass = Mass;
        item.Turned = (radii, inner, _stepMm);
        return item;
    }

    /// <summary>A point nearer the bore than the outside reads the bore.</summary>
    public static string BoreOrOutside(HandToolKind gauge, float pointRadiusMm, float outerMm, float innerMm) =>
        innerMm > 0f && Mathf.Abs(pointRadiusMm - innerMm) < Mathf.Abs(pointRadiusMm - outerMm)
            ? Gauges.Diameter(gauge, 2f * innerMm) is string bore ? "İç çap: " + bore : null
            : Gauges.Diameter(gauge, 2f * outerMm);
}
