using Godot;
using CncTrainer.Spec;

namespace CncTrainer.Procedural;

public static class ToolMeshBuilder
{
    public const float HolderLength = ToolLengthModel.HolderLengthMm * WorldScale.MmToM;

    /// <summary>A mill tool in its holder: tip at the node origin, axis
    /// along +Y, the spindle nose at LengthMm + HolderLength.</summary>
    public static Node3D MillTool(ToolSpec tool)
    {
        var root = new Node3D { Name = "Tool" };
        var cutterMaterial = BodyParts.Mat(tool.Color, 0.7f, 0.25f);
        float radius = tool.DiameterMm / 2f * WorldScale.MmToM;
        float length = tool.LengthMm * WorldScale.MmToM;

        float tipHeight = 0f;
        bool formed = tool.Geometry is { MillProfileMm.Length: >= 2 } && !tool.IsLatheTool;
        if (formed)
        {
            var form = tool.Geometry;
            var profile = form.MillProfileMm;
            root.AddChild(RealToolMeshes.Revolved("Cutter", profile, cutterMaterial));
            float top = profile[^1].Y * WorldScale.MmToM;
            if (length > top)
                BodyParts.Cylinder(root, "Shank", Mathf.Max(profile[^1].X * WorldScale.MmToM, 0.002f), length - top,
                    new Vector3(0f, (length + top) / 2f, 0f), Vector3.Up, cutterMaterial);
        }
        else if (tool.HasPoint)
        {
            tipHeight = Mathf.Min(length, radius / Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(tool.TipAngleDeg, 10f, 170f) / 2f)));
            BodyParts.Cylinder(root, "Point", 0f, tipHeight, new Vector3(0f, tipHeight / 2f, 0f), Vector3.Up, cutterMaterial, topRadius: radius);
        }
        else if (tool.Kind == ToolKind.EndMill && tool.TipRadiusMm > 0f)
        {
            float corner = Mathf.Min(tool.TipRadiusMm * WorldScale.MmToM, radius);
            tipHeight = corner;
            var ball = new MeshInstance3D
            {
                Name = "Nose",
                Mesh = new SphereMesh { Radius = radius, Height = corner * 2f, RadialSegments = 24, Rings = 8 },
                MaterialOverride = cutterMaterial,
                Position = new Vector3(0f, corner, 0f),
            };
            root.AddChild(ball);
        }

        if (!formed && tool.Kind == ToolKind.FaceMill)
        {
            float bodyHeight = Mathf.Min(length, 0.04f);
            BodyParts.Cylinder(root, "Cutter", radius, bodyHeight, new Vector3(0f, bodyHeight / 2f, 0f), Vector3.Up, BodyParts.DarkSteel);
            RealToolMeshes.FaceMillInserts(root, radius, bodyHeight, tool.Flutes, tool.Color);
            if (length > bodyHeight)
                BodyParts.Cylinder(root, "Arbor", Mathf.Min(radius, 0.02f), length - bodyHeight,
                    new Vector3(0f, (length + bodyHeight) / 2f, 0f), Vector3.Up, BodyParts.Steel);
        }
        else if (!formed && length > tipHeight)
        {
            float fluteTop = Mathf.Min(length, tipHeight + Mathf.Max(radius * 6f, 0.01f));
            int flutes = tool.Kind == ToolKind.Drill ? 2 : Mathf.Max(2, tool.Flutes);
            float helix = tool.Kind == ToolKind.Drill ? 30f : 35f;
            root.AddChild(RealToolMeshes.Fluted("Cutter", radius, tipHeight, fluteTop, flutes, helix, cutterMaterial));
            if (length > fluteTop)
                BodyParts.Cylinder(root, "Shank", radius * 0.97f, length - fluteTop, new Vector3(0f, (length + fluteTop) / 2f, 0f), Vector3.Up, cutterMaterial);
        }

        BodyParts.Cylinder(root, "Holder", 0.02f, HolderLength, new Vector3(0f, length + HolderLength / 2f, 0f), Vector3.Up,
            BodyParts.DarkSteel, topRadius: 0.032f);
        return root;
    }

    /// <summary>A lathe tool whose cutting tip sits at the node origin and
    /// whose holder extends along +Y (away from the spindle centreline).
    /// Boring bars and drills lie along +X instead, pointing at the chuck.</summary>
    public static Node3D LatheTool(ToolSpec tool, float shankLength)
    {
        if (tool.LatheProfile != null)
            return RealToolMeshes.LatheTool(tool, shankLength);
        var root = new Node3D { Name = $"T{tool.Number}" };
        var insertMaterial = BodyParts.Mat(tool.Color, 0.3f, 0.3f);
        const float shank = 0.012f;

        switch (tool.Kind)
        {
            case ToolKind.Boring:
            case ToolKind.Drill:
            {
                float radius = Mathf.Max(0.003f, tool.DiameterMm / 2f * WorldScale.MmToM);
                float length = tool.LengthMm * WorldScale.MmToM;
                bool drill = tool.Kind == ToolKind.Drill;
                float point = drill ? radius / Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(tool.TipAngleDeg, 30f, 179f) / 2f)) : 0f;
                if (drill)
                    BodyParts.Cylinder(root, "Point", 0f, point, new Vector3(point / 2f, 0f, 0f), Vector3.Right, insertMaterial, topRadius: radius);
                else
                    BodyParts.Box(root, "Insert", new Vector3(0.006f, 0.006f, 0.004f), new Vector3(0.003f, -0.003f, 0f), insertMaterial);
                var barCenter = new Vector3(point + (length - point) / 2f, drill ? 0f : -radius, 0f);
                BodyParts.Cylinder(root, "Bar", radius, length - point, barCenter, Vector3.Right, drill ? insertMaterial : BodyParts.DarkSteel);
                BodyParts.Box(root, "Block", new Vector3(0.04f, shankLength, 0.04f),
                    new Vector3(length + 0.02f, barCenter.Y + shankLength / 2f - 0.02f, 0f), BodyParts.DarkSteel);
                break;
            }
            case ToolKind.Parting:
            {
                float width = Mathf.Max(0.001f, tool.WidthMm * WorldScale.MmToM);
                BodyParts.Box(root, "Blade", new Vector3(width, 0.03f, 0.02f), new Vector3(0f, 0.015f, 0f), insertMaterial);
                BodyParts.Box(root, "Shank", new Vector3(shank, shankLength - 0.03f, 0.02f),
                    new Vector3(0f, 0.03f + (shankLength - 0.03f) / 2f, 0f), BodyParts.DarkSteel);
                break;
            }
            default:
            {
                float insert = tool.Kind == ToolKind.Threading ? 0.006f : 0.009f;
                var tip = BodyParts.Box(root, "Insert", new Vector3(insert, insert, 0.004f),
                    new Vector3(0f, insert * 0.7071f, 0f), insertMaterial);
                tip.RotationDegrees = new Vector3(0f, 0f, 45f);
                BodyParts.Box(root, "Shank", new Vector3(shank, shankLength - insert, shank),
                    new Vector3(0f, insert + (shankLength - insert) / 2f, 0f), BodyParts.DarkSteel);
                break;
            }
        }
        return root;
    }
}
