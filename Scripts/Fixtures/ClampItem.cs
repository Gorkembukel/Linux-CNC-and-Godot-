using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>One step clamp with its stud, T-nut, step block and flange
/// nut, carried to the stock as a set.</summary>
public partial class ClampItem : Carryable
{
    public static ClampItem Create()
    {
        var clamp = new ClampItem { Name = "Clamp", DisplayName = "Pabuç takımı (pabuç, saplama, basamak bloğu)", Mass = 0.6f };
        BodyParts.Box(clamp, "Strap", new Vector3(0.07f, 0.012f, 0.03f), new Vector3(0f, 0.006f, 0f), BodyParts.DarkSteel);
        BodyParts.Box(clamp, "StepBlock", new Vector3(0.03f, 0.03f, 0.03f), new Vector3(0.05f, 0.015f, 0f), BodyParts.Steel);
        BodyParts.Cylinder(clamp, "Stud", 0.006f, 0.08f, new Vector3(-0.045f, 0.006f, 0f), Vector3.Right, BodyParts.Steel);
        clamp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.14f, 0.03f, 0.03f) }, Position = new Vector3(0f, 0.015f, 0f) });
        return clamp;
    }
}
