using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Material;
using CncTrainer.Spec;

namespace CncTrainer.Addons;

/// <summary>
/// 2D view of a tool's cutting shape on a millimetre grid. Lathe tools: the
/// insert outline as it cuts (X up = away from the part's axis, Z right =
/// toward the tailstock), the controlled point marked, the part's surface
/// line under it. Mill form cutters: the profile, both sides of the axis.
/// Editable shapes (a Custom insert, a mill profile): click near an edge to
/// add a point, drag a point to move it, right click a point to delete it.
/// </summary>
[Tool]
public partial class ProfileCanvas : Control
{
    public ToolSpec Tool;
    public System.Action Edited;

    private float _pixelsPerMm = 12f;
    private Vector2 _pan;
    private int _dragging = -1;
    private bool _panning;

    private bool IsMill => Tool != null && !Tool.IsLatheTool;
    private bool Editable => Tool?.Geometry != null && (IsMill || Tool.Geometry.Insert == InsertShape.Custom);

    /// <summary>A standard mill tool's side profile (radius, height), from
    /// the tip up: what a form cutter starts from.</summary>
    public static Vector2[] StandardMillProfile(ToolSpec tool)
    {
        float r = tool.DiameterMm / 2f, length = tool.LengthMm;
        if (tool.HasPoint)
        {
            float point = r / Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(tool.TipAngleDeg, 10f, 170f) / 2f));
            return new[] { Vector2.Zero, new Vector2(r, point), new Vector2(r, length) };
        }
        float corner = Mathf.Clamp(tool.TipRadiusMm, 0f, r);
        if (corner <= 0f)
            return new[] { Vector2.Zero, new Vector2(r, 0f), new Vector2(r, length) };
        var points = new List<Vector2> { Vector2.Zero };
        for (int i = 0; i <= 6; i++)
        {
            float a = Mathf.Pi / 2f * i / 6f;
            points.Add(new Vector2(r - corner + corner * Mathf.Sin(a), corner - corner * Mathf.Cos(a)));
        }
        points.Add(new Vector2(r, length));
        return points.ToArray();
    }

    // Editable points in mm, drawing space (right = Z / radius, up = X / height).
    private List<Vector2> Points()
    {
        var g = Tool.Geometry;
        if (IsMill)
            return (g.MillProfileMm.Length > 0 ? g.MillProfileMm : StandardMillProfile(Tool)).ToList();
        if (g.Insert == InsertShape.Custom)
            return g.OutlineMm.Select(p => new Vector2(p.Y, p.X)).ToList();
        return Tool.LatheProfile.Outline.Select(p => new Vector2(p.Y, p.X)).ToList();
    }

    private void Store(List<Vector2> points)
    {
        if (IsMill)
            Tool.Geometry.MillProfileMm = points.ToArray();
        else
            Tool.Geometry.OutlineMm = points.Select(p => new Vector2(p.Y, p.X)).ToArray();
        Edited?.Invoke();
        QueueRedraw();
    }

    private Vector2 ToScreen(Vector2 mm) => Size / 2f + _pan + new Vector2(mm.X, -mm.Y) * _pixelsPerMm;
    private Vector2 ToMm(Vector2 screen) { var v = (screen - Size / 2f - _pan) / _pixelsPerMm; return new Vector2(v.X, -v.Y); }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.12f, 0.13f, 0.15f));
        var grid = new Color(1f, 1f, 1f, 0.06f);
        var min = ToMm(Vector2.Zero);
        var max = ToMm(Size);
        for (int x = Mathf.FloorToInt(min.X); x <= Mathf.CeilToInt(max.X); x++)
            DrawLine(ToScreen(new Vector2(x, max.Y)), ToScreen(new Vector2(x, min.Y)), x % 5 == 0 ? grid * 2f : grid);
        for (int y = Mathf.FloorToInt(max.Y); y <= Mathf.CeilToInt(min.Y); y++)
            DrawLine(ToScreen(new Vector2(min.X, y)), ToScreen(new Vector2(max.X, y)), y % 5 == 0 ? grid * 2f : grid);
        DrawLine(ToScreen(new Vector2(min.X, 0f)), ToScreen(new Vector2(max.X, 0f)), new Color(0.4f, 0.8f, 1f, 0.35f), 1.5f);
        DrawLine(ToScreen(new Vector2(0f, min.Y)), ToScreen(new Vector2(0f, max.Y)), new Color(0.4f, 0.8f, 1f, 0.35f), 1.5f);
        if (Tool?.Geometry == null)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(10f, 20f), "Bu takımın gerçek geometrisi yok (Geometry boş).");
            return;
        }

        var points = Points();
        var fill = new Color(Tool.Geometry.InsertColor, 0.55f);
        var edge = new Color(1f, 0.85f, 0.4f);
        if (IsMill)
        {
            foreach (float side in new[] { 1f, -1f })
            {
                var poly = points.Select(p => ToScreen(new Vector2(side * p.X, p.Y))).ToList();
                if (poly.Count >= 2)
                    DrawPolyline(poly.ToArray(), edge, 2f);
            }
            DrawString(ThemeDB.FallbackFont, new Vector2(10f, 20f), "Freze form kesici: yarıçap (sağ) - uçtan yükseklik (yukarı), mm");
        }
        else
        {
            var poly = points.Select(ToScreen).ToArray();
            if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
                DrawColoredPolygon(poly, fill);
            if (poly.Length >= 2)
                DrawPolyline(poly.Append(poly[0]).ToArray(), edge, 2f);
            DrawString(ThemeDB.FallbackFont, new Vector2(10f, 20f), "Torna kalemi: Z (sağa, gezer puntaya) - X (yukarı, eksenden uzağa), mm. Mavi: parça yüzeyi");
        }
        if (!IsMill)
            DrawCircle(ToScreen(Vector2.Zero), 4f, new Color(1f, 0.3f, 0.3f));
        if (Editable)
            foreach (var p in points)
                DrawCircle(ToScreen(p), 4f, Colors.White);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _pixelsPerMm = Mathf.Min(80f, _pixelsPerMm * 1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _pixelsPerMm = Mathf.Max(2f, _pixelsPerMm / 1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _panning = middle.Pressed;
                break;
            case InputEventMouseMotion motion when _panning:
                _pan += motion.Relative;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left when Editable:
                if (!left.Pressed)
                    _dragging = -1;
                else
                    PressAt(left.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right when Editable:
                var pts = Points();
                int hit = Nearest(pts, right.Position);
                if (hit >= 0 && pts.Count > (IsMill ? 2 : 3))
                {
                    pts.RemoveAt(hit);
                    Store(pts);
                }
                break;
            case InputEventMouseMotion drag when _dragging >= 0:
                var moved = Points();
                var mm = ToMm(drag.Position);
                moved[_dragging] = new Vector2(Mathf.Snapped(mm.X, 0.1f), Mathf.Snapped(mm.Y, 0.1f));
                Store(moved);
                break;
            default:
                return;
        }
        QueueRedraw();
        AcceptEvent();
    }

    private int Nearest(List<Vector2> points, Vector2 screen)
    {
        int best = -1;
        float bestDistance = 10f;
        for (int i = 0; i < points.Count; i++)
        {
            float d = ToScreen(points[i]).DistanceTo(screen);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }

    // Grab a point, or add one on the nearest edge (or at the end of a mill profile).
    private void PressAt(Vector2 screen)
    {
        var points = Points();
        _dragging = Nearest(points, screen);
        if (_dragging >= 0)
            return;
        var mm = ToMm(screen);
        var snapped = new Vector2(Mathf.Snapped(mm.X, 0.1f), Mathf.Snapped(mm.Y, 0.1f));
        int insertAt = points.Count;
        float bestDistance = float.PositiveInfinity;
        int segments = IsMill ? points.Count - 1 : points.Count;
        for (int i = 0; i < segments; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            float d = Geometry2D.GetClosestPointToSegment(mm, a, b).DistanceTo(mm);
            if (d < bestDistance)
            {
                bestDistance = d;
                insertAt = i + 1;
            }
        }
        if (IsMill && points.Count > 0 && snapped.Y > points[^1].Y && bestDistance > 2f)
            insertAt = points.Count;
        points.Insert(insertAt, snapped);
        _dragging = insertAt;
        Store(points);
    }

    /// <summary>What is wrong with the shape, or null.</summary>
    public static string Problem(ToolSpec tool)
    {
        var g = tool?.Geometry;
        if (g == null)
            return null;
        if (!tool.IsLatheTool && g.MillProfileMm.Length > 0)
        {
            var p = g.MillProfileMm;
            if (p.Length < 2)
                return "Form profili en az 2 nokta ister.";
            if (Mathf.Abs(p[0].Y) > 1e-3f)
                return "Form profili uçta (yükseklik 0) başlamalı.";
            for (int i = 1; i < p.Length; i++)
                if (p[i].Y <= p[i - 1].Y)
                    return "Form profilinde yükseklik aşağıdan yukarı artmalı.";
            if (p.Any(q => q.X < 0f))
                return "Form profilinde yarıçap negatif olamaz.";
            return null;
        }
        if (g.Insert != InsertShape.Custom)
            return null;
        var o = g.OutlineMm;
        if (o.Length < 3)
            return "Özel insert en az 3 nokta ister.";
        for (int i = 0; i < o.Length; i++)
            for (int j = i + 2; j < o.Length; j++)
            {
                if (i == 0 && j == o.Length - 1)
                    continue;
                if (Geometry2D.SegmentIntersectsSegment(o[i], o[(i + 1) % o.Length], o[j], o[(j + 1) % o.Length]).VariantType != Variant.Type.Nil)
                    return "Özel insertin kenarları kesişiyor.";
            }
        return null;
    }
}
