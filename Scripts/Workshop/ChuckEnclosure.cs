using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Fixtures;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;

namespace CncTrainer.Workshop;

/// <summary>
/// The chuck enclosure on the cabinet: built like a big lathe's guard, a
/// flat back and a wide curved front, its two curved glass doors sliding
/// round the curve to the sides. Inside, D-shaped shelves with chucks
/// stood along the curve, lit from above. Its own frame: origin at the
/// middle of the back wall's foot, +Z out through the curve.
/// </summary>
public partial class ChuckEnclosure : Node3D
{
    /// <summary>Half the width, how far the curve bulges out from the
    /// straight side panels, side panel depth, height (m).</summary>
    private const float HalfWidth = 0.85f, Bulge = 0.4f, SideDepth = 0.15f, Height = 0.95f;
    private const int ArcSegments = 40;

    private float _radius, _centreZ, _halfAngle;
    private static readonly StandardMaterial3D Shell = BodyParts.Mat(new Color(0.3f, 0.34f, 0.4f), 0.35f, 0.45f);
    private static readonly StandardMaterial3D Frame = BodyParts.Mat(new Color(0.85f, 0.86f, 0.88f), 0.5f, 0.35f);

    public static ChuckEnclosure Create(IEnumerable<ChuckModel> chucks, float boreDepthMm)
    {
        var enclosure = new ChuckEnclosure { Name = "ChuckEnclosure" };
        enclosure.Build(chucks.ToArray(), boreDepthMm);
        return enclosure;
    }

    /// <summary>A point on the curve (radius from the arc's centre, angle
    /// 0 = straight ahead, + to the right) at height y.</summary>
    private Vector3 OnArc(float radius, float angle, float y) =>
        new(radius * Mathf.Sin(angle), y, _centreZ + radius * Mathf.Cos(angle));

    private void Build(ChuckModel[] chucks, float boreDepthMm)
    {
        // The arc through the side panels' front ends and the bulge.
        _radius = (HalfWidth * HalfWidth + Bulge * Bulge) / (2f * Bulge);
        _centreZ = SideDepth + Bulge - _radius;
        _halfAngle = Mathf.Asin(HalfWidth / _radius);

        var body = new StaticBody3D { Name = "Shell" };
        AddChild(body);
        BodyParts.SolidBox(body, "Back", new Vector3(HalfWidth * 2f, Height, 0.02f), new Vector3(0f, Height / 2f, 0.01f), Shell);
        foreach (float side in new[] { -1f, 1f })
            BodyParts.SolidBox(body, "Side", new Vector3(0.02f, Height, SideDepth), new Vector3(side * (HalfWidth - 0.01f), Height / 2f, SideDepth / 2f), Shell);
        // Floor, roof and shelves: D-shaped plates.
        AddPlate(body, "Floor", 0f, 0.025f, Shell);
        AddPlate(body, "Roof", Height - 0.025f, 0.025f, Shell);
        float[] shelves = { 0.025f, Height * 0.36f, Height * 0.68f };
        foreach (float y in shelves.Skip(1))
            AddPlate(body, "Shelf", y - 0.012f, 0.012f, BodyParts.Mat(new Color(0.55f, 0.57f, 0.6f), 0.4f, 0.45f));
        // The fixed curved rails the doors run on, top and bottom.
        foreach (float y in new[] { 0.02f, Height - 0.02f })
            AddChild(new MeshInstance3D { Name = "Rail", Mesh = Band(_radius + 0.03f, -_halfAngle, _halfAngle, y - 0.012f, y + 0.012f), MaterialOverride = Frame });
        AddChild(new OmniLight3D { Position = new Vector3(0f, Height - 0.08f, SideDepth), OmniRange = 1.6f, LightEnergy = 0.6f, LightColor = new Color(1f, 0.97f, 0.9f) });

        foreach (float side in new[] { -1f, 1f })
            AddChild(CurvedDoor.Create(this, side));

        PlaceChucks(chucks, shelves, boreDepthMm);
    }

    /// <summary>Chucks stood on their backs along the curve, shelf by shelf,
    /// biggest at the bottom; a second row further in when a shelf's curve
    /// is full.</summary>
    private void PlaceChucks(ChuckModel[] chucks, float[] shelves, float boreDepthMm)
    {
        var order = chucks.OrderByDescending(c => c.DiameterMm).ToList();
        int shelf = 0;
        float rowRadius = _radius - 0.16f, angle = -_halfAngle + 0.08f;
        foreach (var model in order)
        {
            float d = model.DiameterMm * WorldScale.MmToM;
            float step = (d + 0.03f) / rowRadius;
            if (angle + step > _halfAngle - 0.06f)
            {
                // Next row further in, then the next shelf up.
                if (rowRadius > _radius - 0.3f)
                    rowRadius -= 0.2f;
                else if (shelf < shelves.Length - 1)
                {
                    shelf++;
                    rowRadius = _radius - 0.16f;
                }
                angle = -_halfAngle + 0.08f;
                step = (d + 0.03f) / rowRadius;
            }
            var chuck = ChuckItem.Create(model, new ChuckGrip(model.Dims(boreDepthMm)));
            chuck.Freeze = true;
            AddChild(chuck);
            float at = angle + step / 2f;
            chuck.Position = OnArc(rowRadius, at, shelves[shelf] + 0.002f);
            chuck.Rotation = new Vector3(0f, at, 0f);
            angle += step;
        }
    }

    /// <summary>A D-shaped plate: the back wall's line, the side panels and
    /// the curve, from y to y + thickness, with a box collider for its bulk.</summary>
    private void AddPlate(StaticBody3D body, string name, float y, float thickness, Godot.Material material)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var outline = new List<Vector3> { new(-HalfWidth, 0f, 0f), new(-HalfWidth, 0f, SideDepth) };
        for (int i = 0; i <= ArcSegments; i++)
            outline.Add(OnArc(_radius, -_halfAngle + 2f * _halfAngle * i / ArcSegments, 0f));
        outline.Add(new Vector3(HalfWidth, 0f, 0f));
        var centre = new Vector3(0f, 0f, SideDepth * 0.5f);
        foreach (float h in new[] { 0f, thickness })
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i] + Vector3.Up * h;
                var b = outline[(i + 1) % outline.Count] + Vector3.Up * h;
                var c = centre + Vector3.Up * h;
                if (h > 0f) { st.AddVertex(c); st.AddVertex(b); st.AddVertex(a); }
                else { st.AddVertex(c); st.AddVertex(a); st.AddVertex(b); }
            }
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(b + Vector3.Up * thickness);
            st.AddVertex(a); st.AddVertex(b + Vector3.Up * thickness); st.AddVertex(a + Vector3.Up * thickness);
        }
        st.GenerateNormals();
        var mat = (StandardMaterial3D)material.Duplicate();
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        body.AddChild(new MeshInstance3D { Name = name, Mesh = st.Commit(), MaterialOverride = mat, Position = Vector3.Up * y });
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(HalfWidth * 2f, thickness, SideDepth + Bulge * 0.8f) },
            Position = new Vector3(0f, y + thickness / 2f, (SideDepth + Bulge * 0.8f) / 2f),
        });
    }

    /// <summary>A curved strip of the arc (radius r, angles a0..a1, heights y0..y1).</summary>
    internal ArrayMesh Band(float r, float a0, float a1, float y0, float y1)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int segments = Mathf.Max(2, Mathf.CeilToInt(ArcSegments * (a1 - a0) / (2f * _halfAngle)));
        for (int i = 0; i < segments; i++)
        {
            float u0 = a0 + (a1 - a0) * i / segments, u1 = a0 + (a1 - a0) * (i + 1) / segments;
            Vector3 p00 = OnArc(r, u0, y0), p01 = OnArc(r, u0, y1), p10 = OnArc(r, u1, y0), p11 = OnArc(r, u1, y1);
            st.AddVertex(p00); st.AddVertex(p01); st.AddVertex(p11);
            st.AddVertex(p00); st.AddVertex(p11); st.AddVertex(p10);
        }
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>One curved glass door: half the curve, sliding round it to
    /// its side (a turn about the arc's centre) and back.</summary>
    private partial class CurvedDoor : StaticBody3D, IInteractable
    {
        private bool _open;
        private float _side;
        private Node3D _pivot;

        public static Node3D Create(ChuckEnclosure enclosure, float side)
        {
            float r = enclosure._radius + (side > 0f ? 0.012f : 0.024f), half = enclosure._halfAngle;
            var pivot = new Node3D { Name = "DoorPivot", Position = new Vector3(0f, 0f, enclosure._centreZ) };
            var door = new CurvedDoor { Name = "Door", _side = side, _pivot = pivot, CollisionLayer = Fastener.InteractionLayer | 1, CollisionMask = 0 };
            pivot.AddChild(door);
            // Built about the arc's centre: shift the enclosure's arc to here.
            door.Position = new Vector3(0f, 0f, -enclosure._centreZ);
            float a0 = side > 0f ? 0f : -half, a1 = side > 0f ? half : 0f;
            float h = Height;
            var glass = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.78f, 0.88f, 0.96f, 0.2f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.05f, Metallic = 0.1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var frame = (StandardMaterial3D)ChuckEnclosure.Frame.Duplicate();
            frame.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            door.AddChild(new MeshInstance3D { Name = "Glass", Mesh = enclosure.Band(r, a0 + 0.02f, a1 - 0.02f, 0.06f, h - 0.06f), MaterialOverride = glass });
            foreach (var (y0, y1) in new[] { (0.03f, 0.07f), (h - 0.07f, h - 0.03f) })
                door.AddChild(new MeshInstance3D { Name = "Rail", Mesh = enclosure.Band(r, a0, a1, y0, y1), MaterialOverride = frame });
            foreach (float a in new[] { a0, a1 })
            {
                var p = enclosure.OnArc(r, a, h / 2f);
                BodyParts.Box(door, "Stile", new Vector3(0.03f, h - 0.06f, 0.025f), p, frame).Rotation = new Vector3(0f, a, 0f);
            }
            // A handle near the middle edge, and a collider strip per segment.
            float handleAt = side > 0f ? a0 + 0.06f : a1 - 0.06f;
            BodyParts.Box(door, "Handle", new Vector3(0.025f, 0.25f, 0.03f), enclosure.OnArc(r + 0.02f, handleAt, h * 0.5f), BodyParts.DarkSteel)
                .Rotation = new Vector3(0f, handleAt, 0f);
            int strips = 6;
            for (int i = 0; i < strips; i++)
            {
                float a = a0 + (a1 - a0) * (i + 0.5f) / strips;
                float chord = 2f * r * Mathf.Sin((a1 - a0) / strips / 2f);
                door.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = new Vector3(chord, h - 0.06f, 0.02f) },
                    Transform = new Transform3D(new Basis(Vector3.Up, a), enclosure.OnArc(r, a, h / 2f)),
                });
            }
            return pivot;
        }

        public string Prompt(PlayerController player) =>
            player.Held != null ? null : _open ? "[Tık] Kapıyı kapat" : "[Tık] Kapıyı aç (kavis boyunca kayar)";

        public void Use(PlayerController player)
        {
            if (player.Held != null)
                return;
            _open = !_open;
            float half = Mathf.Asin(HalfWidth / ((HalfWidth * HalfWidth + Bulge * Bulge) / (2f * Bulge)));
            _pivot.CreateTween().TweenProperty(_pivot, "rotation:y", _open ? _side * half * 0.95f : 0f, 0.6)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }
    }
}
