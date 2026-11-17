using System;
using Godot;
using CncTrainer.Material;

namespace CncTrainer.Spec;

/// <summary>
/// Catalogue tools with their real cutting shapes: the lathe ones carry a
/// ToolGeometry (ISO insert, holder approach angle), so what they can turn
/// depends on their shape. Each entry makes a fresh ToolSpec for a tool number.
/// </summary>
public static class ToolLibrary
{
    private static readonly Color Gold = new(0.85f, 0.68f, 0.25f), Black = new(0.12f, 0.12f, 0.13f),
        Blue = new(0.25f, 0.45f, 0.8f), Silver = new(0.75f, 0.76f, 0.78f);

    private static ToolSpec Insert(int number, ToolKind kind, string description, InsertShape shape, float edge, float nose, float approach,
        Color color, int orientation = 1, ToolHand hand = ToolHand.Right, float width = 3f, float depth = 20f, float diameter = 0f, float length = 50f)
    {
        var tool = new ToolSpec(number, kind, description, diameter, nose, lengthMm: length, widthMm: width, orientation: orientation, color: color);
        tool.Geometry = new ToolGeometry
        {
            Insert = shape, EdgeLengthMm = edge, NoseRadiusMm = nose, ApproachDeg = approach, Hand = hand,
            WidthMm = width, DepthMm = depth, InsertColor = color, ThicknessMm = shape == InsertShape.Groove ? width : 4.76f,
        };
        return tool;
    }

    public static readonly (string Label, Func<int, ToolSpec> Create)[] Lathe =
    {
        ("CNMG 120408 95° kaba (C 80°)", n => Insert(n, ToolKind.Turning, "CNMG 120408, 95° roughing", InsertShape.C80, 12.9f, 0.8f, 95f, Gold)),
        ("DNMG 150608 93° (D 55°)", n => Insert(n, ToolKind.Turning, "DNMG 150608, 93° profiling", InsertShape.D55, 15.5f, 0.8f, 93f, Black)),
        ("VBMT 160404 107.5° ince profil (V 35°)", n => Insert(n, ToolKind.Turning, "VBMT 160404, 107.5° fine profiling", InsertShape.V35, 16.6f, 0.4f, 107.5f, Blue)),
        ("VBMT 160404 72.5° nötr kopya (V 35°)", n => Insert(n, ToolKind.Turning, "VBMT 160404, 72.5° neutral copying", InsertShape.V35, 16.6f, 0.4f, 72.5f, Blue, hand: ToolHand.Right)),
        ("TNMG 160408 91° (T 60°)", n => Insert(n, ToolKind.Turning, "TNMG 160408, 91° turning", InsertShape.T60, 16.5f, 0.8f, 91f, Gold)),
        ("SNMG 120408 75° (S 90°)", n => Insert(n, ToolKind.Turning, "SNMG 120408, 75° heavy roughing", InsertShape.S90, 12.7f, 0.8f, 75f, Black)),
        ("WNMG 080408 95° (W 80°)", n => Insert(n, ToolKind.Turning, "WNMG 080408, 95° turning", InsertShape.W80, 8.7f, 0.8f, 95f, Gold)),
        ("RCMT 1204 yuvarlak", n => Insert(n, ToolKind.Turning, "RCMT 1204M0, round", InsertShape.Round, 12f, 6f, 90f, Silver)),
        ("Kanal / kesme 3 mm", n => Insert(n, ToolKind.Parting, "Grooving / parting blade, 3 mm", InsertShape.Groove, 0f, 0.2f, 90f, Gold,
            orientation: 3, hand: ToolHand.Neutral, width: 3f, depth: 20f)),
        ("Diş 60° (metrik)", n => Insert(n, ToolKind.Threading, "Threading 60°, metric", InsertShape.Thread60, 0f, 0.1f, 90f, Silver,
            orientation: 6, hand: ToolHand.Neutral, depth: 4f)),
        ("Bor barı Ø16, CCMT 060204", n => Insert(n, ToolKind.Boring, "S16 boring bar, CCMT 060204", InsertShape.C80, 6.4f, 0.4f, 95f, Gold,
            orientation: 2, diameter: 16f, length: 80f)),
    };

    private static ToolSpec MillTool(int n, ToolKind kind, string description, float diameter, float corner = 0f, float length = 50f, int flutes = 4,
        float angle = 118f, Color? color = null) =>
        new(n, kind, description, diameter, corner, lengthMm: length, tipAngleDeg: angle, color: color ?? Silver) { Flutes = flutes };

    public static readonly (string Label, Func<int, ToolSpec> Create)[] Mill =
    {
        ("Parmak freze Ø10, 4 ağız", n => MillTool(n, ToolKind.EndMill, "10mm 4-flute end mill", 10f, flutes: 4)),
        ("Parmak freze Ø6, 2 ağız", n => MillTool(n, ToolKind.EndMill, "6mm 2-flute end mill", 6f, flutes: 2)),
        ("Top uç Ø8", n => MillTool(n, ToolKind.EndMill, "8mm ball nose", 8f, corner: 4f, flutes: 2)),
        ("Köşe radyuslu Ø10 R1", n => MillTool(n, ToolKind.EndMill, "10mm bull nose R1", 10f, corner: 1f, flutes: 4)),
        ("Yüzey frezesi Ø63, 5 kesici", n => MillTool(n, ToolKind.FaceMill, "63mm face mill, 5 inserts", 63f, length: 40f, flutes: 5, color: Gold)),
        ("Matkap Ø8.5", n => MillTool(n, ToolKind.Drill, "8.5mm drill", 8.5f, length: 80f, flutes: 2)),
        ("Pah kırma Ø10 90°", n => MillTool(n, ToolKind.ChamferMill, "10mm chamfer mill 90°", 10f, angle: 90f, flutes: 3)),
    };
}
