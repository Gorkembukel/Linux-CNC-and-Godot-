using Godot;
using CncTrainer.Chips;
using CncTrainer.Spec;

namespace CncTrainer.Workshop;

/// <summary>
/// What a piece of raw stock is: shape, size and material. Block: X x Y
/// footprint and Z height. Round bar: X = length, Y = diameter. Engine-free
/// so it is unit-testable.
/// </summary>
public readonly record struct StockBlank(StockShape Shape, Vector3 SizeMm, MaterialKind Material)
{
    public float LengthMm => SizeMm.X;
    public float DiameterMm => SizeMm.Y;

    public float VolumeMm3 => Shape == StockShape.Block
        ? SizeMm.X * SizeMm.Y * SizeMm.Z
        : Mathf.Pi * DiameterMm * DiameterMm / 4f * LengthMm;

    public float MassKg => VolumeMm3 * 1e-9f * MaterialPreset.Get(Material).DensityKgPerM3;

    /// <summary>Bounding box in the stock's own frame, metres: X along the
    /// length, Y up.</summary>
    public Vector3 BoundsM => (Shape == StockShape.Block
        ? new Vector3(SizeMm.X, SizeMm.Z, SizeMm.Y)
        : new Vector3(LengthMm, DiameterMm, DiameterMm)) * WorldScale.MmToM;

    public string Describe() => Shape == StockShape.Block
        ? $"{SizeMm.X:0.#} x {SizeMm.Y:0.#} x {SizeMm.Z:0.#} mm"
        : $"Ø{DiameterMm:0.#} x {LengthMm:0.#} mm";

    public static string NameOf(MaterialKind material) => MaterialPreset.Get(material).Name;
}
