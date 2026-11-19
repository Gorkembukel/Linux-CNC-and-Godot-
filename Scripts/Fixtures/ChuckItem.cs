using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>A chuck off the lathe: its own sizes (<see cref="ChuckModel"/>)
/// and its own state (<see cref="ChuckGrip"/>: the jaws or collet in it go
/// with it). Clicked onto a lathe's chuck once that one's cam locks are
/// undone, it takes its place.</summary>
public partial class ChuckItem : Carryable
{
    public ChuckModel Model { get; private set; }
    public ChuckGrip Grip { get; private set; }

    private const float M = WorldScale.MmToM;

    public static ChuckItem Create(ChuckModel model, ChuckGrip grip)
    {
        var item = new ChuckItem { Model = model, Grip = grip, Name = "Chuck", DisplayName = model.DisplayName, Mass = model.MassKg };
        float radius = model.DiameterMm / 2f * M, body = model.BodyLengthMm * M;
        // Standing on its back face, jaws up.
        BodyParts.Cylinder(item, "Body", radius, body, new Vector3(0f, body / 2f, 0f), Vector3.Up, BodyParts.Steel);
        float jawH = model.JawDepthMm * 0.7f * M;
        if (model.Kind == ChuckKind.Collet)
            BodyParts.Cylinder(item, "Nose", radius * 0.55f, jawH, new Vector3(0f, body + jawH / 2f, 0f), Vector3.Up, BodyParts.DarkSteel, topRadius: radius * 0.4f);
        else
            for (int i = 0; i < grip.Jaws.SlotCount; i++)
            {
                if (grip.Jaws.Slot(i) is not SeatedJaw jaw)
                    continue;
                float angle = Mathf.Tau * i / grip.Jaws.SlotCount;
                BodyParts.Box(item, "Jaw", new Vector3(radius * 0.25f, jawH, radius * 0.25f),
                    new Vector3(Mathf.Cos(angle) * radius * 0.6f, body + jawH / 2f, Mathf.Sin(angle) * radius * 0.6f),
                    BodyParts.Mat(JawItem.ColorOf(jaw.Kind), 0.7f, 0.4f));
            }
        item.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = body + jawH }, Position = new Vector3(0f, (body + jawH) / 2f, 0f) });
        return item;
    }
}
