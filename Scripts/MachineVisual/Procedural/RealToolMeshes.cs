using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Material;
using CncTrainer.Spec;

namespace CncTrainer.Procedural;

/// <summary>
/// Tools as they look in a catalogue: an insert of its real outline seated
/// in a holder, a grooving blade in its block, a boring bar with its insert;
/// fluted end mills and drills. Frames as ToolMeshBuilder: lathe tools with
/// the controlled point at the origin, X along the spindle (toward the
/// tailstock), Y away from the centreline; mill tools tip at the origin,
/// axis along +Y.
/// </summary>
public static class RealToolMeshes
{
    private const float M = WorldScale.MmToM;

    private static StandardMaterial3D Coated(Color color) => new()
    {
        AlbedoColor = color,
        Metallic = 0.6f,
        Roughness = 0.3f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A lathe tool with a real insert (ToolSpec.Geometry set).</summary>
    public static Node3D LatheTool(ToolSpec tool, float shankLength)
    {
        var root = new Node3D { Name = $"T{tool.Number}" };
        var g = tool.Geometry;
        var profile = tool.LatheProfile;
        var outline = profile.Outline.Select(p => new Vector2(p.Y, p.X) * M).ToList(); // (node X, node Y)
        float thickness = Mathf.Max(0.5f, g.ThicknessMm) * M;
        var insertMat = Coated(g.InsertColor);
        var body = BodyParts.DarkSteel;

        float zLen = profile.ZMax * M, height = profile.Height * M;
        bool boring = tool.Kind == ToolKind.Boring;
        bool blade = g.Insert == InsertShape.Groove;
        root.AddChild(Extrude("Insert", outline, -thickness, 0f, insertMat));

        if (boring)
        {
            float radius = Mathf.Max(0.004f, tool.DiameterMm / 2f * M);
            float length = tool.LengthMm * M;
            var barCentre = new Vector3(zLen + length / 2f, -height * 0.5f - radius * 0.6f, -thickness / 2f);
            BodyParts.Cylinder(root, "Bar", radius, length, barCentre, Vector3.Right, body);
            BodyParts.Box(root, "Block", new Vector3(0.04f, shankLength, 0.04f),
                new Vector3(zLen + length + 0.02f, barCentre.Y + shankLength / 2f - 0.02f, -thickness / 2f), body);
            return root;
        }

        if (blade)
        {
            float bladeTop = height + 0.006f;
            BodyParts.Box(root, "BladeBody", new Vector3(zLen * 0.9f, bladeTop - height * 0.6f, thickness * 0.8f),
                new Vector3(zLen / 2f, (bladeTop + height * 0.6f) / 2f, -thickness / 2f), BodyParts.Steel);
            BodyParts.Box(root, "BladeBlock", new Vector3(0.025f, shankLength - bladeTop, 0.025f),
                new Vector3(zLen / 2f, bladeTop + (shankLength - bladeTop) / 2f, -0.0125f), body);
            return root;
        }

        BodyParts.Box(root, "Seat", new Vector3(zLen * 0.9f, height * 0.8f, 0.004f),
            new Vector3(zLen * 0.55f, height * 0.55f, -thickness - 0.002f), body);
        BodyParts.Box(root, "Clamp", new Vector3(0.008f, 0.008f, 0.003f),
            new Vector3(zLen * 0.55f, height * 0.6f, 0.0015f), BodyParts.Steel);
        float headTop = height + 0.008f;
        BodyParts.Box(root, "Head", new Vector3(zLen + 0.006f, headTop - height * 0.5f, 0.02f),
            new Vector3(zLen * 0.6f, (headTop + height * 0.5f) / 2f, -thickness - 0.01f), body);
        const float shank = 0.02f;
        BodyParts.Box(root, "Shank", new Vector3(shank, shankLength - headTop, shank),
            new Vector3(zLen * 0.6f + shank / 2f, headTop + (shankLength - headTop) / 2f, -thickness - 0.01f), body);
        return root;
    }

    /// <summary>A flat polygon (node X, node Y) as a prism between two Z levels.</summary>
    public static MeshInstance3D Extrude(string name, IList<Vector2> polygon, float zLow, float zHigh, Godot.Material material)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var triangles = Geometry2D.TriangulatePolygon(polygon.ToArray());
        foreach (float z in new[] { zLow, zHigh })
            for (int i = 0; i < triangles.Length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    var p = polygon[triangles[i + k]];
                    st.AddVertex(new Vector3(p.X, p.Y, z));
                }
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            Vector3 a0 = new(a.X, a.Y, zLow), a1 = new(a.X, a.Y, zHigh), b0 = new(b.X, b.Y, zLow), b1 = new(b.X, b.Y, zHigh);
            st.AddVertex(a0); st.AddVertex(b0); st.AddVertex(b1);
            st.AddVertex(a0); st.AddVertex(b1); st.AddVertex(a1);
        }
        st.GenerateNormals();
        return new MeshInstance3D { Name = name, Mesh = st.Commit(), MaterialOverride = material };
    }

    /// <summary>A fluted cutting body from height `from` to `to` along +Y:
    /// the flutes are gullets cut into a round body, twisted by the helix.</summary>
    public static MeshInstance3D Fluted(string name, float radius, float from, float to, int flutes, float helixDeg, Godot.Material material)
    {
        const int around = 48, rings = 24;
        float twistPerMetre = Mathf.Tan(Mathf.DegToRad(helixDeg)) / Mathf.Max(radius, 1e-4f);
        float R(float t) => radius * (0.78f + 0.22f * Mathf.Pow(Mathf.Abs(Mathf.Cos(t * Mathf.Max(1, flutes) / 2f)), 0.35f));
        Vector3 At(int ring, int i)
        {
            float y = from + (to - from) * ring / rings;
            float t = Mathf.Tau * i / around;
            float r = R(t);
            float a = t + (y - from) * twistPerMetre;
            return new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int ring = 0; ring < rings; ring++)
            for (int i = 0; i < around; i++)
            {
                var p00 = At(ring, i); var p01 = At(ring, i + 1); var p10 = At(ring + 1, i); var p11 = At(ring + 1, i + 1);
                st.AddVertex(p00); st.AddVertex(p11); st.AddVertex(p10);
                st.AddVertex(p00); st.AddVertex(p01); st.AddVertex(p11);
            }
        foreach (var (ring, y) in new[] { (0, from), (rings, to) })
            for (int i = 0; i < around; i++)
            {
                st.AddVertex(new Vector3(0f, y, 0f)); st.AddVertex(At(ring, i)); st.AddVertex(At(ring, i + 1));
            }
        st.GenerateNormals();
        var mat = material.Duplicate() as StandardMaterial3D ?? new StandardMaterial3D();
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return new MeshInstance3D { Name = name, Mesh = st.Commit(), MaterialOverride = mat };
    }

    /// <summary>A mill form cutter: its side profile (radius, height above
    /// the tip, mm) turned about +Y.</summary>
    public static MeshInstance3D Revolved(string name, Vector2[] profileMm, Godot.Material material)
    {
        const int around = 48;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 At(Vector2 p, int i)
        {
            float t = Mathf.Tau * i / around;
            return new Vector3(p.X * M * Mathf.Cos(t), p.Y * M, p.X * M * Mathf.Sin(t));
        }
        for (int k = 0; k < profileMm.Length - 1; k++)
            for (int i = 0; i < around; i++)
            {
                Vector3 p00 = At(profileMm[k], i), p01 = At(profileMm[k], i + 1), p10 = At(profileMm[k + 1], i), p11 = At(profileMm[k + 1], i + 1);
                st.AddVertex(p00); st.AddVertex(p11); st.AddVertex(p10);
                st.AddVertex(p00); st.AddVertex(p01); st.AddVertex(p11);
            }
        foreach (var end in new[] { profileMm[0], profileMm[^1] })
            for (int i = 0; i < around; i++)
            {
                st.AddVertex(new Vector3(0f, end.Y * M, 0f)); st.AddVertex(At(end, i)); st.AddVertex(At(end, i + 1));
            }
        st.GenerateNormals();
        var mat = material.Duplicate() as StandardMaterial3D ?? new StandardMaterial3D();
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return new MeshInstance3D { Name = name, Mesh = st.Commit(), MaterialOverride = mat };
    }

    /// <summary>A face mill body with inserts around its rim.</summary>
    public static void FaceMillInserts(Node3D parent, float radius, float bodyHeight, int inserts, Color color)
    {
        var mat = Coated(color);
        for (int i = 0; i < Mathf.Max(3, inserts); i++)
        {
            float a = Mathf.Tau * i / Mathf.Max(3, inserts);
            var box = BodyParts.Box(parent, "Insert", new Vector3(0.004f, 0.009f, 0.009f),
                new Vector3(Mathf.Cos(a) * (radius - 0.003f), 0.0045f, Mathf.Sin(a) * (radius - 0.003f)), mat);
            box.Basis = new Basis(Vector3.Up, -a);
        }
    }
}
