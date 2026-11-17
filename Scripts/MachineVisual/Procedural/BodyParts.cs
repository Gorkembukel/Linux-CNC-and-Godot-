using Godot;

namespace CncTrainer.Procedural;

/// <summary>Primitive builders shared by the procedural machine bodies.
/// Sizes and positions are metres in the parent's frame.</summary>
public static class BodyParts
{
    public static StandardMaterial3D Mat(Color color, float metallic = 0.2f, float rough = 0.6f) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = rough,
    };

    // Properties, not static fields: a static Godot object keeps a [Tool]
    // assembly from unloading when the editor rebuilds it.
    public static StandardMaterial3D Steel => Mat(new Color(0.55f, 0.56f, 0.58f), 0.85f, 0.3f);
    public static StandardMaterial3D DarkSteel => Mat(new Color(0.2f, 0.2f, 0.22f), 0.7f, 0.45f);

    public static StandardMaterial3D CoolantLook() => new()
    {
        AlbedoColor = new Color(0.75f, 0.9f, 1f, 0.55f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.05f,
    };

    public static MeshInstance3D Box(Node3D parent, string name, Vector3 size, Vector3 center, Godot.Material material)
    {
        var mesh = new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = center };
        parent.AddChild(mesh);
        return mesh;
    }

    /// <summary>A box the player cannot walk through. Only for parts that
    /// never move.</summary>
    public static void SolidBox(Node3D parent, string name, Vector3 size, Vector3 center, Godot.Material material)
    {
        var body = new StaticBody3D { Name = name, Position = center };
        parent.AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
    }

    /// <summary>Cylinder (or cone, with a different top radius) whose axis
    /// runs along `axis` through `center`.</summary>
    public static MeshInstance3D Cylinder(Node3D parent, string name, float radius, float length, Vector3 center, Vector3 axis,
        Godot.Material material, float topRadius = -1f)
    {
        var mesh = new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh { BottomRadius = radius, TopRadius = topRadius < 0f ? radius : topRadius, Height = length, RadialSegments = 24 },
            MaterialOverride = material,
            Position = center,
            Basis = BasisWithY(axis),
        };
        parent.AddChild(mesh);
        return mesh;
    }

    public static Basis BasisWithY(Vector3 axis)
    {
        var y = axis.Normalized();
        var x = (Mathf.Abs(y.Y) < 0.99f ? Vector3.Up : Vector3.Right).Cross(y).Normalized();
        return new Basis(x, y, x.Cross(y));
    }

    public static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
