using Godot;

namespace CncTrainer;

/// <summary>
/// Brief section 1 ("look like a real machine") + user request: a primitive-
/// built lathe body (bed, headstock, chuck, carriage) since no suitable
/// CC0/CC-BY model was found (see ASSETS.md for the search). Built
/// procedurally in code, matching this project's existing style for
/// generated geometry (WorkpieceVisual, ChipSpawner) rather than hand-tuned
/// .tscn transforms.
///
/// World-axis convention (matches Main.cs/WorkpieceVisual): world X = machine
/// Z (spindle axis), world Y = machine X (radius, 0 = centerline), world Z is
/// the unused third dimension, used here only to give the body visual depth.
/// </summary>
public partial class MachineBody : Node3D
{
    [Export] public float ChuckEndWorldX = 5f; // matches Main's StockZMax
    [Export] public float BedFarEndWorldX = -140f;
    [Export] public float BedNearEndWorldX = 45f;

    private MeshInstance3D _carriage;

    public override void _Ready()
    {
        var steelGray = Mat(new Color(0.42f, 0.44f, 0.47f), metallic: 0.6f, rough: 0.5f);
        var darkGray = Mat(new Color(0.22f, 0.23f, 0.25f), metallic: 0.5f, rough: 0.6f);
        var chuckMetal = Mat(new Color(0.55f, 0.56f, 0.58f), metallic: 0.85f, rough: 0.3f);
        var machineGreen = Mat(new Color(0.15f, 0.35f, 0.22f), metallic: 0.2f, rough: 0.6f); // common machine-tool paint

        // Bed: long box below the spindle centerline.
        float bedLength = BedNearEndWorldX - BedFarEndWorldX;
        AddBox(
            size: new Vector3(bedLength, 30f, 70f),
            center: new Vector3((BedFarEndWorldX + BedNearEndWorldX) / 2f, -60f, 0f),
            material: machineGreen, name: "Bed");

        // Headstock: chunky box at the chuck end, spanning above and below centerline.
        float headstockCenterX = ChuckEndWorldX + 24f;
        AddBox(
            size: new Vector3(48f, 90f, 80f),
            center: new Vector3(headstockCenterX, 0f, 0f),
            material: machineGreen, name: "Headstock");

        // Chuck: short cylinder right at the stock's chuck-end face, bigger
        // radius than the stock so the jaws read as "holding" it.
        var chuck = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 32f, BottomRadius = 32f, Height = 18f },
            MaterialOverride = chuckMetal,
            Name = "Chuck",
            Position = new Vector3(ChuckEndWorldX - 9f, 0f, 0f),
            // CylinderMesh's axis is Y by default; rotate so it points along world X (the spindle axis).
            RotationDegrees = new Vector3(0f, 0f, 90f),
        };
        AddChild(chuck);

        // Carriage: rides the bed, repositioned along X every frame to track
        // the tool's Z position (see UpdateCarriage) -- a simplification
        // (doesn't model the cross-slide's own separate radial motion).
        _carriage = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(26f, 32f, 55f) },
            MaterialOverride = darkGray,
            Name = "Carriage",
            Position = new Vector3(0f, -29f, 0f),
        };
        AddChild(_carriage);

        _ = steelGray; // reserved for future detail pieces (tailstock, guard)
    }

    /// <summary>Call every frame with the tool's current world-X (= machine Z).</summary>
    public void UpdateCarriage(float toolWorldX)
    {
        _carriage.Position = new Vector3(toolWorldX, _carriage.Position.Y, 0f);
    }

    private void AddBox(Vector3 size, Vector3 center, Godot.Material material, string name)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = center,
            Name = name,
        };
        AddChild(mi);
    }

    private static StandardMaterial3D Mat(Color color, float metallic, float rough) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = rough,
    };
}
