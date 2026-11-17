using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Workshop;

/// <summary>Workbench carrying one of every hand tool. The node's origin is
/// on the floor.</summary>
[Tool]
public partial class ToolBench : Node3D
{
    private const float TopY = 0.9f;

    public override void _Ready()
    {
        BodyParts.Clear(this);
        var wood = BodyParts.Mat(new Color(0.5f, 0.36f, 0.2f), 0f, 0.85f);
        BodyParts.SolidBox(this, "Top", new Vector3(1.6f, 0.05f, 0.6f), new Vector3(0f, TopY - 0.025f, 0f), wood);
        foreach (float x in new[] { -0.72f, 0.72f })
            foreach (float z in new[] { -0.24f, 0.24f })
                BodyParts.SolidBox(this, "Leg", new Vector3(0.06f, TopY - 0.05f, 0.06f), new Vector3(x, (TopY - 0.05f) / 2f, z), BodyParts.DarkSteel);
        AddChild(new Label3D
        {
            Text = "ALETLER",
            PixelSize = 0.002f,
            FontSize = 40,
            Position = new Vector3(0f, TopY - 0.1f, 0.301f),
            Modulate = new Color(0.9f, 0.9f, 0.9f),
        });

        if (Engine.IsEditorHint())
            return;
        var kinds = System.Enum.GetValues<HandToolKind>();
        for (int i = 0; i < kinds.Length; i++)
        {
            var tool = HandTool.Create(kinds[i]);
            // Frozen until first picked up so nothing rolls off the bench.
            tool.Freeze = true;
            AddChild(tool);
            tool.Position = new Vector3(-0.68f + 1.36f * i / (kinds.Length - 1), TopY + 0.06f, i % 2 == 0 ? -0.1f : 0.12f);
        }
    }
}
