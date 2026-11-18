using Godot;
using CncTrainer.Procedural;

namespace CncTrainer.Interaction;

public enum HandToolKind { HexKey, ViseHandle, ChuckKey, Mallet, Brush, Parallels, PaperGauge, Caliper, DialIndicator, Micrometer, DepthGauge }

/// <summary>A hand tool from the bench. What it does is decided by the thing
/// it is used on (a fastener asks for a HexKey, a chuck for a ChuckKey...).</summary>
public partial class HandTool : Carryable
{
    public HandToolKind Kind { get; private set; }

    /// <summary>In a holster of the player's belt (taken from there, not picked up).</summary>
    public bool Stowed { get; set; }

    public override bool CanPickUp => !Stowed;

    public static string NameOf(HandToolKind kind) => kind switch
    {
        HandToolKind.HexKey => "T kollu alyan",
        HandToolKind.ViseHandle => "Mengene kolu",
        HandToolKind.ChuckKey => "Ayna anahtarı",
        HandToolKind.Mallet => "Plastik çekiç",
        HandToolKind.Brush => "Fırça",
        HandToolKind.Parallels => "Paralel takımı",
        HandToolKind.PaperGauge => "Kağıt mastar (0,05 mm)",
        HandToolKind.Caliper => "Kumpas",
        HandToolKind.Micrometer => "Mikrometre",
        HandToolKind.DepthGauge => "Derinlik mastarı",
        _ => "Komparatör",
    };

    public static HandTool Create(HandToolKind kind)
    {
        var tool = new HandTool { Kind = kind, DisplayName = NameOf(kind), Name = kind.ToString(), Mass = 0.4f };
        var steel = BodyParts.Steel;
        var dark = BodyParts.DarkSteel;
        var size = new Vector3(0.2f, 0.03f, 0.05f);
        switch (kind)
        {
            case HandToolKind.HexKey:
                BodyParts.Cylinder(tool, "Shaft", 0.004f, 0.16f, new Vector3(0f, 0f, 0f), Vector3.Right, steel);
                BodyParts.Cylinder(tool, "Handle", 0.012f, 0.1f, new Vector3(-0.08f, 0f, 0f), Vector3.Back, BodyParts.Mat(new Color(0.75f, 0.1f, 0.1f)));
                size = new Vector3(0.18f, 0.03f, 0.1f);
                break;
            case HandToolKind.ViseHandle:
                BodyParts.Cylinder(tool, "Bar", 0.009f, 0.24f, Vector3.Zero, Vector3.Right, steel);
                BodyParts.Box(tool, "Socket", new Vector3(0.035f, 0.03f, 0.03f), new Vector3(0.12f, 0f, 0f), dark);
                size = new Vector3(0.28f, 0.03f, 0.03f);
                break;
            case HandToolKind.ChuckKey:
                BodyParts.Cylinder(tool, "Shaft", 0.007f, 0.12f, Vector3.Zero, Vector3.Up, steel);
                BodyParts.Cylinder(tool, "Handle", 0.006f, 0.14f, new Vector3(0f, 0.06f, 0f), Vector3.Right, steel);
                BodyParts.Box(tool, "Square", new Vector3(0.011f, 0.02f, 0.011f), new Vector3(0f, -0.065f, 0f), dark);
                size = new Vector3(0.14f, 0.14f, 0.02f);
                break;
            case HandToolKind.Mallet:
                BodyParts.Cylinder(tool, "Handle", 0.012f, 0.26f, Vector3.Zero, Vector3.Right, BodyParts.Mat(new Color(0.55f, 0.4f, 0.22f), 0f, 0.8f));
                BodyParts.Cylinder(tool, "Head", 0.028f, 0.1f, new Vector3(0.13f, 0f, 0f), Vector3.Back, BodyParts.Mat(new Color(0.9f, 0.85f, 0.2f), 0f, 0.7f));
                size = new Vector3(0.3f, 0.06f, 0.1f);
                tool.Mass = 0.7f;
                break;
            case HandToolKind.Brush:
                BodyParts.Box(tool, "Handle", new Vector3(0.14f, 0.012f, 0.03f), Vector3.Zero, BodyParts.Mat(new Color(0.55f, 0.4f, 0.22f), 0f, 0.8f));
                BodyParts.Box(tool, "Bristles", new Vector3(0.05f, 0.03f, 0.03f), new Vector3(0.09f, -0.01f, 0f), BodyParts.Mat(new Color(0.1f, 0.1f, 0.1f), 0f, 1f));
                size = new Vector3(0.2f, 0.04f, 0.03f);
                break;
            case HandToolKind.Parallels:
                foreach (float side in new[] { -1f, 1f })
                    BodyParts.Box(tool, "Parallel", new Vector3(0.15f, 0.03f, 0.004f), new Vector3(0f, 0f, side * 0.006f), steel);
                size = new Vector3(0.15f, 0.03f, 0.02f);
                break;
            case HandToolKind.PaperGauge:
                BodyParts.Box(tool, "Slip", new Vector3(0.1f, 0.001f, 0.03f), Vector3.Zero, BodyParts.Mat(new Color(0.95f, 0.95f, 0.9f), 0f, 0.9f));
                size = new Vector3(0.1f, 0.01f, 0.03f);
                tool.Mass = 0.05f;
                break;
            case HandToolKind.Caliper:
                BodyParts.Box(tool, "Beam", new Vector3(0.22f, 0.004f, 0.016f), Vector3.Zero, steel);
                BodyParts.Box(tool, "FixedJaw", new Vector3(0.006f, 0.004f, 0.05f), new Vector3(-0.107f, 0f, 0.03f), steel);
                BodyParts.Box(tool, "SlidingJaw", new Vector3(0.03f, 0.006f, 0.05f), new Vector3(-0.06f, 0f, 0.025f), dark);
                size = new Vector3(0.22f, 0.01f, 0.07f);
                break;
            case HandToolKind.Micrometer:
                BodyParts.Box(tool, "Frame", new Vector3(0.08f, 0.012f, 0.05f), new Vector3(-0.02f, 0f, 0.02f), dark);
                BodyParts.Cylinder(tool, "Spindle", 0.004f, 0.05f, new Vector3(0f, 0f, 0f), Vector3.Right, steel);
                BodyParts.Cylinder(tool, "Thimble", 0.009f, 0.06f, new Vector3(0.07f, 0f, 0f), Vector3.Right, steel);
                size = new Vector3(0.17f, 0.02f, 0.06f);
                break;
            case HandToolKind.DepthGauge:
                BodyParts.Box(tool, "Base", new Vector3(0.1f, 0.006f, 0.016f), Vector3.Zero, steel);
                BodyParts.Box(tool, "Rod", new Vector3(0.004f, 0.12f, 0.004f), new Vector3(0f, 0.02f, 0f), steel);
                BodyParts.Box(tool, "Slider", new Vector3(0.03f, 0.03f, 0.008f), new Vector3(0f, 0.03f, 0f), dark);
                size = new Vector3(0.1f, 0.12f, 0.02f);
                break;
            default:
                BodyParts.Box(tool, "MagBase", new Vector3(0.05f, 0.04f, 0.04f), Vector3.Zero, dark);
                BodyParts.Cylinder(tool, "Arm", 0.004f, 0.14f, new Vector3(0.03f, 0.08f, 0f), new Vector3(0.4f, 1f, 0f), steel);
                BodyParts.Cylinder(tool, "Dial", 0.025f, 0.012f, new Vector3(0.06f, 0.15f, 0f), Vector3.Back, BodyParts.Mat(new Color(0.9f, 0.9f, 0.9f)));
                size = new Vector3(0.1f, 0.18f, 0.04f);
                tool.Mass = 0.9f;
                break;
        }
        tool.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return tool;
    }
}
