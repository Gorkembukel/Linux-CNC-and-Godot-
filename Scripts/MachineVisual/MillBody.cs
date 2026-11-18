using Godot;

namespace CncTrainer;

/// <summary>
/// Primitive-built gantry mill: the stock stays fixed on the table and the
/// spindle moves in all three axes (beam along machine Y, carriage along X,
/// quill along Z). Local axes match MillStockVisual: origin at the centre of
/// the stock's top face, X = machine X, Y (up) = machine Z, Z = -machine Y.
/// </summary>
[Tool]
public partial class MillBody : Node3D
{
    /// <summary>Height of the table top relative to the stock's top face (m): the underside of a 30 mm stock.</summary>
    [Export] public float TableTopY = -0.03f;
    /// <summary>Height of the gantry beam above the stock's top face (m).</summary>
    [Export] public float BeamY = 0.37f;

    private const float ToolStickout = 0.05f;
    private const float QuillHeight = 0.3f;

    private Node3D _beam, _carriage, _quill;
    private MeshInstance3D _tool;
    private float _toolDiameterMm = -1f;

    public override void _Ready()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        var machineBlue = Mat(new Color(0.16f, 0.27f, 0.42f), metallic: 0.2f, rough: 0.6f);
        var darkGray = Mat(new Color(0.22f, 0.23f, 0.25f), metallic: 0.5f, rough: 0.6f);
        var steel = Mat(new Color(0.55f, 0.56f, 0.58f), metallic: 0.85f, rough: 0.3f);

        float tableThickness = 0.04f;
        float cabinetTopY = TableTopY - tableThickness;
        AddStaticBox(new Vector3(0.6f, cabinetTopY - WorldScale.FloorY, 0.5f),
            new Vector3(0f, (cabinetTopY + WorldScale.FloorY) / 2f, 0f), machineBlue, "Cabinet");
        AddStaticBox(new Vector3(0.5f, tableThickness, 0.36f),
            new Vector3(0f, TableTopY - tableThickness / 2f, 0f), steel, "Table");

        // Vise jaws along the stock's long sides, kept lower than the
        // deepest cut the demo program makes.
        foreach (float side in new[] { -1f, 1f })
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.018f, 0.02f) },
                MaterialOverride = darkGray,
                Position = new Vector3(0f, TableTopY + 0.009f, side * 0.05f),
                Name = side < 0 ? "ViseJawFar" : "ViseJawNear",
            });

        float railTopY = BeamY - 0.04f;
        foreach (float side in new[] { -1f, 1f })
            AddStaticBox(new Vector3(0.05f, railTopY - cabinetTopY, 0.5f),
                new Vector3(side * 0.325f, (railTopY + cabinetTopY) / 2f, 0f), machineBlue, side < 0 ? "RailLeft" : "RailRight");

        _beam = new Node3D { Name = "Beam" };
        AddChild(_beam);
        _beam.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.08f, 0.08f) },
            MaterialOverride = machineBlue,
            Position = new Vector3(0f, BeamY, -0.1f),
        });

        _carriage = new Node3D { Name = "Carriage" };
        _beam.AddChild(_carriage);
        _carriage.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.14f, 0.06f) },
            MaterialOverride = darkGray,
            Position = new Vector3(0f, BeamY, -0.03f),
        });

        _quill = new Node3D { Name = "Quill" };
        _carriage.AddChild(_quill);
        _quill.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = QuillHeight },
            MaterialOverride = steel,
            Position = new Vector3(0f, ToolStickout + QuillHeight / 2f, 0f),
        });
        _tool = new MeshInstance3D
        {
            MaterialOverride = Mat(new Color(0.85f, 0.75f, 0.35f), metallic: 0.7f, rough: 0.25f),
            Position = new Vector3(0f, ToolStickout / 2f, 0f),
            Visible = false,
            Name = "Tool",
        };
        _quill.AddChild(_tool);

        UpdateTool(new Vector3(0f, 0.05f, 0f), 10f);
    }

    /// <summary>Call every frame with the tool tip's position in this
    /// machine's local space (metres) and the active cutter's diameter.</summary>
    public void UpdateTool(Vector3 tipLocal, float toolDiameterMm)
    {
        _beam.Position = new Vector3(0f, 0f, tipLocal.Z);
        _carriage.Position = new Vector3(tipLocal.X, 0f, 0f);
        _quill.Position = new Vector3(0f, tipLocal.Y, 0f);

        if (toolDiameterMm == _toolDiameterMm)
            return;
        _toolDiameterMm = toolDiameterMm;
        _tool.Visible = toolDiameterMm > 0f;
        float radius = toolDiameterMm / 2f * WorldScale.MmToM;
        _tool.Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = ToolStickout, RadialSegments = 16 };
    }

    private void AddStaticBox(Vector3 size, Vector3 center, Godot.Material material, string name)
    {
        var body = new StaticBody3D { Name = name, Position = center };
        AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
    }

    private static StandardMaterial3D Mat(Color color, float metallic, float rough) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = rough,
    };
}
