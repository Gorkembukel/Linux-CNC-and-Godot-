using Godot;
using CncTrainer.Interaction;
using CncTrainer.Machining;
using CncTrainer.Player;
using CncTrainer.Procedural;

namespace CncTrainer.Workshop;

/// <summary>Cart with one machine's tools and a tool length gauge. The
/// machine starts with none of them in it: the player carries each to the
/// spindle or the turret. The node's origin is on the floor.</summary>
[Tool]
public partial class ToolCart : Node3D
{
    private const float TopY = 0.85f;
    private ToolLengthGauge _gauge;

    /// <summary>The ProceduralMachine whose tools this cart carries.</summary>
    [Export] public NodePath Machine;

    public override void _Ready()
    {
        BodyParts.Clear(this);
        BodyParts.SolidBox(this, "Top", new Vector3(1.0f, 0.04f, 0.5f), new Vector3(0f, TopY - 0.02f, 0f), BodyParts.DarkSteel);
        BodyParts.SolidBox(this, "Frame", new Vector3(0.9f, TopY - 0.04f, 0.4f), new Vector3(0f, (TopY - 0.04f) / 2f, 0f), BodyParts.Mat(new Color(0.1f, 0.3f, 0.6f)));
        AddChild(new Label3D
        {
            Text = "TAKIM ARABASI",
            PixelSize = 0.002f,
            FontSize = 36,
            Position = new Vector3(0f, TopY - 0.2f, 0.201f),
            Modulate = new Color(0.95f, 0.95f, 0.95f),
        });
        _gauge = ToolLengthGauge.Create(new Vector3(0.38f, TopY, 0f));
        AddChild(_gauge);

        if (!Engine.IsEditorHint())
            CallDeferred(MethodName.Fill);
    }

    // Deferred: the machine sets itself up in its own _Ready.
    private void Fill()
    {
        if (GetNodeOrNull(Machine ?? "") is not ProceduralMachine { Spec: not null } machine)
        {
            GD.PrintErr($"[ToolCart] '{Name}': Machine is not set to a ProceduralMachine.");
            return;
        }
        _gauge.Lengths = machine.Spec.ToolLengths();
        int i = 0;
        foreach (var spec in machine.Spec.Tools)
        {
            if (spec == null)
                continue;
            var tool = ToolItem.Create(spec, lathe: machine.Spec.Kind == Spec.MachineKind.Lathe);
            // Frozen until first picked up so nothing rolls off the cart.
            tool.Freeze = true;
            AddChild(tool);
            tool.Position = new Vector3(-0.42f + 0.09f * (i % 8), TopY, -0.12f + 0.2f * (i / 8));
            i++;
        }
    }
}

/// <summary>Height gauge on the tool cart: stand a mill tool on it and it
/// reads the tool's length. A broken tool handed to it is swapped for a new
/// one, which again has its own length.</summary>
public partial class ToolLengthGauge : StaticBody3D, IInteractable
{
    /// <summary>The cart's machine: decides which number goes in its tool table.</summary>
    public Spec.ToolLengthModel Lengths;

    public static ToolLengthGauge Create(Vector3 at)
    {
        var gauge = new ToolLengthGauge { Name = "LengthGauge", Position = at };
        BodyParts.Box(gauge, "Base", new Vector3(0.14f, 0.02f, 0.14f), new Vector3(0f, 0.01f, 0f), BodyParts.Steel);
        BodyParts.Cylinder(gauge, "Column", 0.01f, 0.25f, new Vector3(0.05f, 0.145f, -0.05f), Vector3.Up, BodyParts.Steel);
        BodyParts.Box(gauge, "Readout", new Vector3(0.06f, 0.04f, 0.02f), new Vector3(0.05f, 0.25f, -0.05f), BodyParts.Mat(new Color(0.1f, 0.5f, 0.2f)));
        gauge.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.14f, 0.28f, 0.14f) }, Position = new Vector3(0f, 0.14f, 0f) });
        return gauge;
    }

    public string Prompt(PlayerController player)
    {
        if (player.Held is not ToolItem tool)
            return "Takım boyu ölçer (elinde bir takımla bak)";
        if (tool.State.Broken)
            return "[Tık] Kırık takımı yenisiyle değiştir";
        if (tool.ForLathe)
            return "Torna kalemi burada ölçülmez: deneme pasosu al, kumpasla ölç, TOUCH OFF";
        float gaugeLength = tool.TrueLengthMm + Spec.ToolLengthModel.HolderLengthMm;
        float table = Lengths.TableLengthMm(tool.Spec.LengthMm, tool.ErrorMm.Y);
        return System.FormattableString.Invariant(
            $"Burun-uç boyu: {gaugeLength:0.00} mm   (nominal {tool.Spec.LengthMm + Spec.ToolLengthModel.HolderLengthMm:0.00}, fark {tool.ErrorMm.Y:+0.00;-0.00})   OFS'e: Z{table:0.00}   aşınma %{tool.State.Wear * 100f:0}");
    }

    public void Use(PlayerController player)
    {
        if (player.Held is not ToolItem { State.Broken: true } broken)
            return;
        var fresh = ToolItem.Create(broken.Spec, lathe: broken.ForLathe);
        GetTree().CurrentScene.AddChild(fresh);
        player.ReleaseHeld().QueueFree();
        player.Give(fresh);
    }
}
