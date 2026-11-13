using Godot;

namespace CncTrainer;

/// <summary>
/// Brief section 1 ("look like a real machine") + user request: a primitive-
/// built lathe body (floor, bed/cabinet, headstock, chuck, carriage) since no
/// suitable CC0/CC-BY model was found (see ASSETS.md for the search).
///
/// [Tool]: runs in the EDITOR too, not just at play time -- a plain runtime
/// script only generates its meshes once the game actually runs, so opening
/// the scene in the editor showed nothing at all ("editörden bakınca bir
/// anlamı yok" -- exactly right, and the reason for this attribute). Anything
/// that needs the running game (the BridgeClient autoload) is guarded by
/// Engine.IsEditorHint() so it only runs at play time; the geometry itself
/// builds either way.
///
/// World units are meters (see WorldScale.cs) -- this was mm-scale before,
/// which is incompatible with any future human-scale character controller
/// (Godot physics defaults, like gravity and character speeds, assume
/// roughly real-world units). World-axis convention matches Main.cs: world X
/// = machine Z (spindle axis), world Y = machine X (radius, 0 = centerline),
/// world Z is the unused third dimension (depth).
/// </summary>
[Tool]
public partial class MachineBody : Node3D
{
    [Export] public float ChuckEndWorldX = 0.005f; // = StockZMax(5mm) * WorldScale.MmToM
    [Export] public float BedFarEndWorldX = -0.4f;
    [Export] public float BedNearEndWorldX = 0.15f;

    private MeshInstance3D _carriage;

    public override void _Ready()
    {
        RebuildGeometry();
    }

    private void RebuildGeometry()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        var machineGreen = Mat(new Color(0.15f, 0.35f, 0.22f), metallic: 0.2f, rough: 0.6f); // common machine-tool paint
        var darkGray = Mat(new Color(0.22f, 0.23f, 0.25f), metallic: 0.5f, rough: 0.6f);
        var chuckMetal = Mat(new Color(0.55f, 0.56f, 0.58f), metallic: 0.85f, rough: 0.3f);
        var floorMat = Mat(new Color(0.35f, 0.35f, 0.37f), metallic: 0.05f, rough: 0.9f);

        // Floor: a human (planned: FP/TP controller) needs something to stand
        // on and collide with. Large flat slab, top surface at WorldScale.FloorY.
        AddStaticBox(
            size: new Vector3(6f, 0.1f, 6f),
            center: new Vector3(0f, WorldScale.FloorY - 0.05f, 0f),
            material: floorMat, name: "Floor");

        // Bed/cabinet: one box from the floor up to just below the spindle
        // centerline -- doubles as the machine's stand, simplifying geometry
        // and collision into a single piece.
        float bedLength = BedNearEndWorldX - BedFarEndWorldX;
        float bedTopY = -0.08f;
        AddStaticBox(
            size: new Vector3(bedLength, bedTopY - WorldScale.FloorY, 0.3f),
            center: new Vector3((BedFarEndWorldX + BedNearEndWorldX) / 2f, (bedTopY + WorldScale.FloorY) / 2f, 0f),
            material: machineGreen, name: "BedCabinet");

        // Headstock: chunky box at the chuck end, spanning above and below centerline.
        float headstockCenterX = ChuckEndWorldX + 0.15f;
        AddStaticBox(
            size: new Vector3(0.25f, 0.2f, 0.3f),
            center: new Vector3(headstockCenterX, bedTopY + 0.1f, 0f),
            material: machineGreen, name: "Headstock");

        // Chuck: cylinder at the stock's chuck-end face, bigger radius than
        // the stock so the jaws read as "holding" it. Deliberately overlaps
        // ~25mm INTO the stock's own Z range (not just butted flush against
        // it) -- real jaws grip a length of the bar, hiding it entirely; with
        // a flush, zero-overlap join, a facing cut right at that single
        // shared point visibly thinned the stock to nothing exactly at the
        // chuck, looking like the bar had snapped off there (reported live).
        // Decorative only (no collision -- small, central, not in a walking path).
        const float chuckOverlapIntoStock = 0.03f;
        var chuck = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = chuckOverlapIntoStock + 0.005f },
            MaterialOverride = chuckMetal,
            Name = "Chuck",
            Position = new Vector3(ChuckEndWorldX - chuckOverlapIntoStock / 2f + 0.0025f, 0f, 0f),
            // CylinderMesh's axis is Y by default; rotate so it points along world X (the spindle axis).
            RotationDegrees = new Vector3(0f, 0f, 90f),
        };
        AddChild(chuck);

        // Carriage: rides the bed, repositioned along X every frame to track
        // the tool's Z position (see UpdateCarriage) -- a simplification
        // (doesn't model the cross-slide's own separate radial motion).
        // Decorative only; small and keeps pace with the tool, not worth
        // colliding against.
        _carriage = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.06f, 0.12f) },
            MaterialOverride = darkGray,
            Name = "Carriage",
            Position = new Vector3(0f, bedTopY + 0.03f, 0f),
        };
        AddChild(_carriage);
    }

    /// <summary>Call every frame (play mode only) with the tool's current world-X (= machine Z).</summary>
    public void UpdateCarriage(float toolWorldX)
    {
        if (_carriage == null)
            return;
        _carriage.Position = new Vector3(toolWorldX, _carriage.Position.Y, 0f);
    }

    private void AddStaticBox(Vector3 size, Vector3 center, Godot.Material material, string name)
    {
        // Deliberately NOT setting Owner on these: that would make the editor
        // serialize them into Main.tscn on save, and since RebuildGeometry()
        // always wipes and regenerates from code anyway, a saved copy would
        // be dead weight at best (and a source of confusing diffs). Leaving
        // Owner unset keeps them out of the Scene dock tree and off disk,
        // while they still render normally in both the editor viewport and
        // at runtime -- Owner only controls persistence/dock listing, not
        // whether a node is part of the live tree or gets drawn.
        var body = new StaticBody3D { Name = name, Position = center };
        AddChild(body);

        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Name = name + "Mesh" };
        body.AddChild(mi);

        var col = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Name = name + "Collision" };
        body.AddChild(col);
    }

    private static StandardMaterial3D Mat(Color color, float metallic, float rough) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = rough,
    };
}
