using Godot;
using CncTrainer.Fixtures;
using CncTrainer.Procedural;

namespace CncTrainer.Workshop;

/// <summary>Cart of workholding the player takes to a machine: vise and its
/// bolts, step clamps, centring plate. What it carries is set per instance.
/// The node's origin is on the floor.</summary>
[Tool]
public partial class FixtureCart : Node3D
{
    private const float TopY = 0.75f;

    [Export] public bool Vise = true;
    [Export] public bool CentringPlate;
    [Export] public int Bolts = 4;
    [Export] public int Clamps = 4;

    public override void _Ready()
    {
        BodyParts.Clear(this);
        BodyParts.SolidBox(this, "Top", new Vector3(0.9f, 0.04f, 0.55f), new Vector3(0f, TopY - 0.02f, 0f), BodyParts.Steel);
        BodyParts.SolidBox(this, "Frame", new Vector3(0.8f, TopY - 0.04f, 0.45f), new Vector3(0f, (TopY - 0.04f) / 2f, 0f), BodyParts.Mat(new Color(0.75f, 0.1f, 0.1f)));
        AddChild(new Label3D
        {
            Text = "BAĞLAMA ARABASI",
            PixelSize = 0.002f,
            FontSize = 36,
            Position = new Vector3(0f, TopY - 0.2f, 0.226f),
            Modulate = new Color(0.95f, 0.95f, 0.95f),
        });

        if (Engine.IsEditorHint())
            return;
        // Frozen until first picked up so nothing slides off the cart.
        if (Vise)
            Put(Fixtures.Vise.Create(), new Vector3(-0.28f, TopY, 0f));
        if (CentringPlate)
            Put(Fixtures.CentringPlate.Create(), new Vector3(Vise ? 0f : -0.25f, TopY, 0f));
        for (int i = 0; i < Bolts; i++)
            Put(BoltItem.Create(), new Vector3(0.2f + 0.05f * (i % 4), TopY, -0.2f + 0.06f * (i / 4)));
        for (int i = 0; i < Clamps; i++)
            Put(ClampItem.Create(), new Vector3(0.28f, TopY, -0.08f + 0.05f * i), yawDeg: 0f);
    }

    private void Put(RigidBody3D item, Vector3 at, float yawDeg = 0f)
    {
        item.Freeze = true;
        AddChild(item);
        item.Position = at;
        item.RotationDegrees = new Vector3(0f, yawDeg, 0f);
    }
}
