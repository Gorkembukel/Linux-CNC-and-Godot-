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
    // ChuckEndWorldX = StockZMin(-85mm) * WorldScale.MmToM. The chuck grips
    // the end the demo program treats as deepest/raw stock; its own motion
    // (face at Z0 -> turn OD to Z-60 -> part at Z-55) proves Z0 is the free,
    // exposed tip, not the chuck face, despite that program's own comment
    // claiming the opposite -- found live (reported: cutting looked like it
    // tore material straight off the chuck, because this used to sit at
    // StockZMax, right where facing/turning/parting actually happen).
    [Export] public float ChuckEndWorldX = -0.085f;
    [Export] public float BedFarEndWorldX = -0.45f;
    [Export] public float BedNearEndWorldX = 0.15f;

    private const int StationCount = 3; // matches linuxcnc-config/sim_lathe.tbl's T1/T2/T3 exactly -- no speculative empty station
    [Export] public float TurretToolReachMm = 45f; // distance from turret center to a tool's insert tip
    [Export] public float TurretIndexSpeedDegPerSec = 240f;

    private MeshInstance3D _carriage;
    private Node3D _turret;
    private float _turretIndexAngleDeg = 180f; // station 0 starts active (tool 1), matching _turretTargetAngleDeg so it doesn't spin on the very first frame
    private float _turretTargetAngleDeg = 180f;
    private int _lastToolNumber = -1;

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

        // Headstock: chunky box at the chuck end, spanning above and below
        // centerline. Offset AWAY from the stock (more negative, past the
        // chuck) -- mirrors the pre-fix layout, just on the other end.
        float headstockCenterX = ChuckEndWorldX - 0.15f;
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
            // Overlaps toward INCREASING Z from StockZMin (mirror of the old
            // StockZMax-anchored math, which overlapped toward decreasing Z).
            Position = new Vector3(ChuckEndWorldX + chuckOverlapIntoStock / 2f - 0.0025f, 0f, 0f),
            // CylinderMesh's axis is Y by default; rotate so it points along world X (the spindle axis).
            RotationDegrees = new Vector3(0f, 0f, 90f),
        };
        AddChild(chuck);

        // Carriage: rides the bed, repositioned along X every frame to track
        // the tool's Z position (see UpdateCarriage). Decorative only; small
        // and keeps pace with the tool, not worth colliding against. Carries
        // the turret (below), which DOES now model the cross-slide's radial
        // motion -- the carriage box itself still doesn't move in Y, only
        // the turret riding on it does.
        _carriage = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.06f, 0.12f) },
            MaterialOverride = darkGray,
            Name = "Carriage",
            Position = new Vector3(0f, bedTopY + 0.03f, 0f),
        };
        AddChild(_carriage);

        BuildTurret(darkGray);
    }

    /// <summary>Real tool turret, replacing the old flat "red cube" tool
    /// marker: a small indexing disc (child of the carriage, so it inherits
    /// the carriage's X/Z tracking) with one station per real tool in
    /// sim_lathe.tbl, each a fixed shank+insert built once here and never
    /// touched again -- only the turret's own transform animates per frame
    /// (see UpdateTool). T1/T2 (turning tools) get different insert colors
    /// so the active one reads at a glance; T3 gets a wider insert, matching
    /// its "3mm wide" parting-tool note in sim_lathe.tbl.</summary>
    private void BuildTurret(Godot.Material shankMaterial)
    {
        const float turretBodyRadius = 0.035f;
        float reachWorld = TurretToolReachMm * WorldScale.MmToM;

        _turret = new Node3D { Name = "Turret", RotationDegrees = new Vector3(_turretIndexAngleDeg, 0f, 0f) };
        _carriage.AddChild(_turret);

        _turret.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = turretBodyRadius, BottomRadius = turretBodyRadius, Height = 0.03f },
            MaterialOverride = shankMaterial,
            // CylinderMesh's axis is Y by default; rotate so its indexing
            // axis points along world X (the spindle axis), same pattern as
            // the chuck cylinder above.
            RotationDegrees = new Vector3(0f, 0f, 90f),
        });

        (Color insertColor, float shankWidthMm, float insertSizeMm)[] stations =
        {
            (new Color(0.85f, 0.65f, 0.2f), 10f, 6f),  // T1 roughing -- amber carbide-look insert
            (new Color(0.3f, 0.8f, 0.9f), 8f, 5f),     // T2 finishing -- cyan, visually distinct from T1
            (new Color(0.9f, 0.9f, 0.95f), 6f, 10f),   // T3 parting -- wider insert (it's the 3mm-wide parting tool)
        };

        for (int i = 0; i < stations.Length; i++)
        {
            var (insertColor, shankWidthMm, insertSizeMm) = stations[i];
            var pivot = new Node3D { RotationDegrees = new Vector3(i * (360f / StationCount), 0f, 0f) };
            _turret.AddChild(pivot);

            float shankWidth = shankWidthMm * WorldScale.MmToM;
            float shankLength = reachWorld - turretBodyRadius;
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(shankWidth, shankLength, shankWidth) },
                MaterialOverride = shankMaterial,
                Position = new Vector3(0f, turretBodyRadius + shankLength / 2f, 0f),
            });

            float insertSize = insertSizeMm * WorldScale.MmToM;
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(shankWidth, insertSize, shankWidth) },
                MaterialOverride = Mat(insertColor, metallic: 0.3f, rough: 0.3f),
                Position = new Vector3(0f, reachWorld - insertSize / 2f, 0f),
            });
        }
    }

    /// <summary>Call every frame (play mode only) with the tool's current world-X (= machine Z).</summary>
    public void UpdateCarriage(float toolWorldX)
    {
        if (_carriage == null)
            return;
        _carriage.Position = new Vector3(toolWorldX, _carriage.Position.Y, 0f);
    }

    /// <summary>Call every frame (play mode only) with the tool's current
    /// world-Y (= machine X / radius) and the bridge's reported tool
    /// number. Indexes the turret toward whichever tool is active and keeps
    /// the turret's own Y such that the active station's insert lands
    /// exactly on toolTipWorldY, at any radius/retract distance -- the role
    /// a real cross-slide plays (see BuildTurret's doc comment).</summary>
    public void UpdateTool(float toolTipWorldY, int toolNumber, double delta)
    {
        if (_turret == null)
            return;

        if (toolNumber != _lastToolNumber && toolNumber >= 1 && toolNumber <= StationCount)
        {
            _lastToolNumber = toolNumber;
            _turretTargetAngleDeg = TargetIndexAngleDeg(toolNumber, StationCount);
        }

        _turretIndexAngleDeg = StepAngleToward(_turretIndexAngleDeg, _turretTargetAngleDeg, TurretIndexSpeedDegPerSec * (float)delta);
        _turret.RotationDegrees = new Vector3(_turretIndexAngleDeg, 0f, 0f);

        float reachWorld = TurretToolReachMm * WorldScale.MmToM;
        _turret.Position = new Vector3(0f, toolTipWorldY + reachWorld, 0f);
    }

    /// <summary>Rotate the turret so station (toolNumber-1)%stationCount's
    /// fixed local +Y direction (where its insert sits) ends up pointing
    /// world -Y (toward the centerline). Pure math (Godot.Mathf only, no
    /// engine objects) -- unit-testable without the engine, same pattern as
    /// LatheRadiusProfile/WorkpiecePartBuilder.</summary>
    public static float TargetIndexAngleDeg(int toolNumber, int stationCount) =>
        180f - ((toolNumber - 1) % stationCount) * (360f / stationCount);

    /// <summary>Rate-limited step from `current` toward `target`, taking the
    /// shorter way around the 0/360 wrap.</summary>
    public static float StepAngleToward(float current, float target, float maxStepDeg)
    {
        float diff = Mathf.Wrap(target - current, -180f, 180f);
        return current + Mathf.Clamp(diff, -maxStepDeg, maxStepDeg);
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
