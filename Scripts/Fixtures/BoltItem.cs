using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>A hold-down bolt with its T-nut, carried to a bolt hole one at
/// a time.</summary>
public partial class BoltItem : Carryable, Save.ISavedItem
{
    public Godot.Collections.Dictionary SaveState() => new();

    public static BoltItem Create()
    {
        var bolt = new BoltItem { Name = "Bolt", DisplayName = "T-somun + cıvata", Mass = 0.15f };
        BodyParts.Cylinder(bolt, "Shank", 0.006f, 0.06f, new Vector3(0f, 0.03f, 0f), Vector3.Up, BodyParts.DarkSteel);
        BodyParts.Cylinder(bolt, "Head", 0.011f, 0.012f, new Vector3(0f, 0.066f, 0f), Vector3.Up, BodyParts.DarkSteel);
        BodyParts.Box(bolt, "TNut", new Vector3(0.03f, 0.012f, 0.02f), new Vector3(0f, 0.006f, 0f), BodyParts.Steel);
        bolt.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.03f, 0.072f, 0.022f) }, Position = new Vector3(0f, 0.036f, 0f) });
        return bolt;
    }
}
