using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Fixtures;
using CncTrainer.Spec;
using static CncTrainer.Procedural.BodyParts;

namespace CncTrainer.Procedural;

/// <summary>
/// Builds a mill from a MachineSpec: one Node3D per axis, nested in chain
/// order, with the castings that axis carries as its children. Local frame:
/// origin = work zero, X = machine X, Y (up) = machine Z, Z = -machine Y.
/// Everything is laid out with all axes at 0, where the tool tip is at the
/// origin; SetPose then only moves the link nodes.
/// </summary>
[Tool]
public partial class ProceduralMillBody : Node3D
{
    private const float M = WorldScale.MmToM;

    private MachineSpec _spec;
    private readonly List<(AxisDef Axis, Node3D Node)> _links = new();
    private Node3D _toolNode, _spindle, _headMount;
    private bool _headFollowsToolLength;
    private float _longestTool;
    private ToolSpec _shownTool;
    private bool _toolShown;
    private float _toolDrop;

    /// <summary>Moves with the workpiece: parent for the stock.</summary>
    public Node3D WorkEnd { get; private set; }
    /// <summary>Moves with the spindle; its origin is the nominal tool tip.</summary>
    public Node3D ToolEnd { get; private set; }

    /// <summary>Table top in WorkEnd's frame, its size, and where its
    /// T-slots run (along X, at these Z).</summary>
    public float TableTopY { get; private set; }
    public float TableSizeX { get; private set; }
    public float TableSizeZ { get; private set; }
    public float[] SlotZ { get; private set; }
    /// <summary>The enclosure's open front (centre and size, this body's
    /// frame), null on an open machine.</summary>
    public (Vector3 Centre, Vector2 Size)? DoorOpening { get; private set; }

    public void Build(MachineSpec spec)
    {
        _spec = spec;
        Clear(this);
        _links.Clear();
        _toolShown = false;

        var m = spec.Mill;
        var axes = spec.AxisDefs();
        var bodyMat = Mat(spec.BodyColor);
        var accent = Mat(spec.AccentColor, 0.5f);
        float floorY = -spec.WorkHeightMm * M;

        var workLinks = AddChain(axes.Where(a => a.OnWorkpiece), "Work");
        var toolLinks = AddChain(axes.Where(a => !a.OnWorkpiece), "Tool");
        WorkEnd = workLinks.Count > 0 ? workLinks[^1].Node : this;
        var toolEnd = ToolEnd = toolLinks.Count > 0 ? toolLinks[^1].Node : this;

        float stockH = m.StockHeight * M, stockX = m.StockSizeXMm * M, stockZ = m.StockSizeYMm * M;
        float tableX = m.TableSizeXMm * M, tableZ = m.TableSizeYMm * M, tableT = m.TableThicknessMm * M;

        // ---- workpiece side, from the stock downward ----
        float cursor = -stockH;
        // With manual setup the fixture is a real object the player brings;
        // only the height it will take up is kept so the table stays put.
        switch (spec.ManualSetup ? Workholding.None : m.Workholding)
        {
            case Workholding.Vise:
                float jawH = Mathf.Min(stockH * 0.6f, 0.04f);
                foreach (float side in new[] { -1f, 1f })
                    Box(WorkEnd, "ViseJaw", new Vector3(Mathf.Min(stockX, 0.16f), jawH, 0.025f),
                        new Vector3(0f, cursor + jawH / 2f, side * (stockZ / 2f + 0.0125f)), accent);
                Box(WorkEnd, "ViseBody", new Vector3(Mathf.Min(stockX, 0.16f) + 0.02f, 0.05f, stockZ + 0.1f),
                    new Vector3(0f, cursor - 0.025f, 0f), accent);
                cursor -= 0.05f;
                break;
            case Workholding.Clamps:
                float clampH = Mathf.Min(stockH * 0.5f, 0.02f);
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                        Box(WorkEnd, "Clamp", new Vector3(0.03f, clampH, 0.05f),
                            new Vector3(sx * (stockX / 2f - 0.02f), cursor + clampH / 2f, sz * (stockZ / 2f + 0.025f)), accent);
                break;
            case Workholding.Chuck:
                Cylinder(WorkEnd, "Chuck", stockH / 2f + 0.03f, 0.05f,
                    new Vector3(-stockX / 2f - 0.015f, -stockH / 2f, 0f), Vector3.Right, Steel);
                break;
        }

        if (spec.ManualSetup && m.Workholding == Workholding.Vise)
            cursor -= 0.05f;
        // On a rotary table the stock goes on a centring plate.
        if (spec.ManualSetup && m.Workholding == Workholding.Clamps && axes.Any(a => a.OnWorkpiece && a.IsRotary))
            cursor -= CentringPlate.Thickness;
        TableTopY = cursor;
        TableSizeX = tableX;
        TableSizeZ = tableZ;
        SlotZ = Enumerable.Range(0, m.TSlotCount).Select(i => -tableZ / 2f + tableZ * (i + 1) / (m.TSlotCount + 1)).ToArray();

        bool tableMade = false;
        float footX = tableX, footZ = tableZ;
        AxisId? linkAbove = null;
        for (int k = workLinks.Count - 1; k >= 0; k--)
        {
            var (axis, node) = workLinks[k];
            Node3D parent = k > 0 ? workLinks[k - 1].Node : this;

            if (!axis.IsRotary)
            {
                var size = new Vector3(tableX, tableT, tableZ);
                if (tableMade)
                    size = linkAbove == AxisId.X ? new Vector3(tableX * 0.5f, tableT, tableZ * 1.1f) : new Vector3(tableX * 1.05f, tableT, tableZ * 0.6f);
                Box(node, tableMade ? "Saddle" : "Table", size, new Vector3(0f, cursor - tableT / 2f, 0f), tableMade ? accent : Steel);
                if (!tableMade)
                    AddTSlots(node, cursor, tableX, tableZ, m.TSlotCount);
                cursor -= tableT;
                tableMade = true;
                linkAbove = axis.Letter;
                continue;
            }

            var dir = MachineKinematics.Direction(axis.Letter);
            var pivot = MachineKinematics.MachineToLocal(axis.PivotMm);
            if (axis.Letter == AxisId.C)
            {
                float radius = Mathf.Min(tableX, tableZ) / 2f;
                if (!tableMade)
                {
                    Cylinder(node, "Platter", radius, 0.04f, new Vector3(pivot.X, cursor - 0.02f, pivot.Z), Vector3.Up, Steel);
                    cursor -= 0.04f;
                    tableMade = true;
                    footX = footZ = radius * 2f;
                }
                Cylinder(parent, "RotaryBase", radius * 0.8f, 0.05f, new Vector3(pivot.X, cursor - 0.025f, pivot.Z), Vector3.Up, accent);
                cursor -= 0.05f;
                continue;
            }

            bool alongX = axis.Letter == AxisId.A;
            Vector3 Sized(float along, float height, float across) => alongX ? new Vector3(along, height, across) : new Vector3(across, height, along);
            Vector3 At(float along, float y) => new Vector3(pivot.X, y, pivot.Z) + dir * along;

            if (m.Workholding == Workholding.Chuck && !tableMade)
            {
                // Indexer: the stock hangs between a driven chuck and a centre.
                float half = stockH / 2f + 0.05f;
                float end = stockX / 2f + 0.04f;
                Box(parent, "Indexer", Sized(0.12f, half * 2f, half * 2f), At(-(end + 0.06f), pivot.Y), bodyMat);
                Box(parent, "Centre", Sized(0.05f, half + 0.03f, 0.08f), At(end + 0.025f, pivot.Y - half / 2f + 0.015f), bodyMat);
                cursor = pivot.Y - half;
                footX = alongX ? (end + 0.12f) * 2f : half * 2f;
                footZ = alongX ? half * 2f : (end + 0.12f) * 2f;
                continue;
            }

            // Trunnion: a cradle hung between two pedestals.
            if (!tableMade)
            {
                Box(node, "Table", new Vector3(tableX, tableT, tableZ), new Vector3(0f, cursor - tableT / 2f, 0f), Steel);
                AddTSlots(node, cursor, tableX, tableZ, m.TSlotCount);
                cursor -= tableT;
                tableMade = true;
            }
            float alongHalf = (alongX ? footX : footZ) / 2f + 0.03f;
            float acrossHalf = (alongX ? footZ : footX) / 2f;
            float armLow = Mathf.Min(cursor, pivot.Y - 0.06f), armHigh = Mathf.Max(cursor, pivot.Y + 0.06f);
            float swing = new Vector2(pivot.Y - cursor, acrossHalf).Length();
            float pedestalLow = pivot.Y - swing - 0.03f;
            foreach (float side in new[] { -1f, 1f })
            {
                Box(node, "CradleArm", Sized(0.04f, armHigh - armLow, 0.14f), At(side * (alongHalf + 0.02f), (armHigh + armLow) / 2f), accent);
                Cylinder(parent, "Journal", 0.05f, 0.06f, At(side * (alongHalf + 0.07f), pivot.Y), dir, Steel);
                Box(parent, "Pedestal", Sized(0.08f, pivot.Y + 0.08f - pedestalLow, 0.2f),
                    At(side * (alongHalf + 0.14f), (pivot.Y + 0.08f + pedestalLow) / 2f), bodyMat);
            }
            float plateAlong = (alongHalf + 0.18f) * 2f, plateAcross = Mathf.Max(acrossHalf * 2f, 0.2f);
            Box(parent, "TrunnionBase", Sized(plateAlong, 0.04f, plateAcross), At(0f, pedestalLow - 0.02f), accent);
            cursor = pedestalLow - 0.04f;
            footX = alongX ? plateAlong : plateAcross;
            footZ = alongX ? plateAcross : plateAlong;
        }
        if (!tableMade)
        {
            Box(this, "Table", new Vector3(tableX, tableT, tableZ), new Vector3(0f, cursor - tableT / 2f, 0f), Steel);
            AddTSlots(this, cursor, tableX, tableZ, m.TSlotCount);
            cursor -= tableT;
        }
        float baseTop = Mathf.Max(cursor, floorY + 0.05f);

        // ---- tool side ----
        _longestTool = spec.Tools.Where(t => t != null).Select(t => t.LengthMm).DefaultIfEmpty(50f).Max() * M;
        var zAxis = spec.FindAxis(AxisId.Z);
        float zMin = (zAxis?.MinLimit ?? 0f) * M, zMax = (zAxis?.MaxLimit ?? 200f) * M;
        float xHalf = TravelHalf(spec.FindAxis(AxisId.X)), yHalf = TravelHalf(spec.FindAxis(AxisId.Y));
        float housingR = m.SpindleHousingDiameterMm / 2f * M;

        var linear = toolLinks.Where(l => !l.Axis.IsRotary).ToList();
        Node3D headCarrier = linear.Count >= 1 ? linear[^1].Node : this;
        Node3D secondCarrier = linear.Count >= 2 ? linear[^2].Node : this;
        Node3D thirdCarrier = linear.Count >= 3 ? linear[^3].Node : this;

        _toolNode = new Node3D { Name = "ToolHolder" };
        toolEnd.AddChild(_toolNode);
        _spindle = new Node3D { Name = "Spindle" };
        toolEnd.AddChild(_spindle);
        Cylinder(_spindle, "Cartridge", housingR, 0.28f, new Vector3(0f, 0.14f, 0f), Vector3.Up, Steel);
        Cylinder(_spindle, "Nose", housingR * 0.75f, 0.02f, new Vector3(0f, -0.01f, 0f), Vector3.Up, DarkSteel);

        // Rotary axes on the tool side: a fork (or turntable) on the link
        // before each one, around its pivot.
        int firstRotary = toolLinks.FindIndex(l => l.Axis.IsRotary);
        _headFollowsToolLength = firstRotary < 0;
        float headAttachY = _longestTool + ToolMeshBuilder.HolderLength;
        for (int i = 0; i < toolLinks.Count; i++)
        {
            var axis = toolLinks[i].Axis;
            if (!axis.IsRotary)
                continue;
            Node3D parent = i > 0 ? toolLinks[i - 1].Node : this;
            var pivot = MachineKinematics.MachineToLocal(axis.PivotMm);
            var dir = MachineKinematics.Direction(axis.Letter);
            if (axis.Letter == AxisId.C)
                Cylinder(parent, "HeadTurntable", housingR + 0.06f, 0.06f, pivot + Vector3.Up * 0.1f, Vector3.Up, accent);
            else
            {
                bool alongX = axis.Letter == AxisId.A;
                foreach (float side in new[] { -1f, 1f })
                    Box(parent, "ForkCheek", alongX ? new Vector3(0.03f, 0.22f, 0.18f) : new Vector3(0.18f, 0.22f, 0.03f),
                        pivot + dir * side * (housingR + 0.035f), accent);
                Box(parent, "ForkBridge", alongX ? new Vector3(housingR * 2f + 0.1f, 0.04f, 0.18f) : new Vector3(0.18f, 0.04f, housingR * 2f + 0.1f),
                    pivot + Vector3.Up * 0.13f, accent);
            }
            if (i == firstRotary)
                headAttachY = pivot.Y + 0.13f;
        }

        _headMount = new Node3D { Name = "Head", Position = new Vector3(0f, headAttachY, 0f) };
        headCarrier.AddChild(_headMount);

        float structureTop;
        float baseWidth = m.BaseWidthMm * M, baseDepth = m.BaseDepthMm * M, baseCenterZ = 0f;
        if (m.Frame == MillFrame.CFrame)
        {
            float throat = tableZ / 2f + yHalf + 0.08f;
            float columnW = m.ColumnWidthMm * M;
            const float columnD = 0.3f;
            float front = housingR + 0.04f;
            Box(_headMount, "HeadCasting", new Vector3(columnW * 0.8f, 0.4f, front + throat),
                new Vector3(0f, 0.23f, (front - throat) / 2f), bodyMat);

            structureTop = headAttachY + Mathf.Max(zMax, 0f) + 0.5f;
            var columnSize = new Vector3(columnW, structureTop - baseTop, columnD);
            var columnCenter = new Vector3(0f, (structureTop + baseTop) / 2f, -throat - columnD / 2f);
            if (secondCarrier == this)
                SolidBox(this, "Column", columnSize, columnCenter, bodyMat);
            else
                Box(secondCarrier, "Column", columnSize, columnCenter, bodyMat);
            AddMagazine(secondCarrier, m.MagazinePockets, new Vector3(-columnW / 2f, structureTop - 0.2f, -throat - columnD / 2f), accent);

            float back = -throat - columnD;
            baseDepth = Mathf.Max(baseDepth, tableZ / 2f - back);
            baseCenterZ = back + baseDepth / 2f;
        }
        else
        {
            float ramLength = zMax - zMin + 0.3f;
            Box(_headMount, "Ram", new Vector3(0.14f, ramLength, 0.14f), new Vector3(0f, 0.05f + ramLength / 2f, 0f), bodyMat);

            float carriageY = headAttachY + 0.05f + zMax + 0.15f;
            float spanHalf = Mathf.Max(tableX / 2f, xHalf + 0.12f) + 0.08f;
            Box(secondCarrier, "Carriage", new Vector3(0.24f, 0.3f, 0.04f), new Vector3(0f, carriageY, -0.09f), accent);
            Box(thirdCarrier, "Beam", new Vector3(spanHalf * 2f + 0.2f, 0.2f, 0.16f), new Vector3(0f, carriageY, -0.19f), bodyMat);
            foreach (float side in new[] { -1f, 1f })
            {
                var size = new Vector3(0.12f, carriageY + 0.1f - baseTop, 0.2f);
                var center = new Vector3(side * (spanHalf + 0.04f), (carriageY + 0.1f + baseTop) / 2f, -0.19f);
                if (thirdCarrier == this)
                    SolidBox(this, "Upright", size, center, bodyMat);
                else
                    Box(thirdCarrier, "Upright", size, center, bodyMat);
            }
            AddMagazine(thirdCarrier, m.MagazinePockets, new Vector3(-spanHalf - 0.1f, carriageY + 0.1f, -0.19f), accent);

            structureTop = carriageY + ramLength;
            baseWidth = Mathf.Max(baseWidth, spanHalf * 2f + 0.2f);
            baseDepth = Mathf.Max(baseDepth, tableZ + yHalf * 2f);
        }

        SolidBox(this, "Base", new Vector3(baseWidth, baseTop - floorY, baseDepth),
            new Vector3(0f, (baseTop + floorY) / 2f, baseCenterZ), bodyMat);

        if (spec.Enclosure)
        {
            var sheet = Mat(new Color(0.8f, 0.81f, 0.83f), 0.1f, 0.7f);
            float w = baseWidth + xHalf * 2f + 0.3f, d = baseDepth + 0.3f, top = structureTop + 0.1f;
            float midY = (top + floorY) / 2f, height = top - floorY;
            SolidBox(this, "EnclosureBack", new Vector3(w, height, 0.02f), new Vector3(0f, midY, baseCenterZ - d / 2f), sheet);
            foreach (float side in new[] { -1f, 1f })
                SolidBox(this, "EnclosureSide", new Vector3(0.02f, height, d), new Vector3(side * w / 2f, midY, baseCenterZ), sheet);
            Box(this, "EnclosureRoof", new Vector3(w, 0.02f, d), new Vector3(0f, top, baseCenterZ), sheet);
            DoorOpening = (new Vector3(0f, midY, baseCenterZ + d / 2f), new Vector2(w, height));
        }
        else
            DoorOpening = null;

        SetPose(AxisVector.Zero);
        SetTool(spec.Tools.FirstOrDefault(t => t != null)?.Number ?? 0);
    }

    private List<(AxisDef Axis, Node3D Node)> AddChain(IEnumerable<AxisDef> axes, string prefix)
    {
        var chain = new List<(AxisDef, Node3D)>();
        Node3D parent = this;
        foreach (var axis in axes)
        {
            var node = new Node3D { Name = prefix + axis.Letter };
            parent.AddChild(node);
            chain.Add((axis, node));
            _links.Add((axis, node));
            parent = node;
        }
        return chain;
    }

    private static float TravelHalf(AxisSpec axis) =>
        axis == null ? 0.1f : Mathf.Max(Mathf.Abs(axis.MinLimit), Mathf.Abs(axis.MaxLimit)) * M;

    private static void AddTSlots(Node3D parent, float topY, float tableX, float tableZ, int count)
    {
        var slot = Mat(new Color(0.08f, 0.08f, 0.09f), 0.3f, 0.8f);
        for (int i = 0; i < count; i++)
            Box(parent, "TSlot", new Vector3(tableX, 0.004f, 0.014f),
                new Vector3(0f, topY - 0.0015f, -tableZ / 2f + tableZ * (i + 1) / (count + 1)), slot);
    }

    private static void AddMagazine(Node3D parent, int pockets, Vector3 mountPoint, Godot.Material material)
    {
        if (pockets <= 0)
            return;
        float radius = Mathf.Clamp(pockets * 0.013f, 0.1f, 0.3f);
        var center = mountPoint + new Vector3(-radius * 0.8f, 0f, 0f);
        Cylinder(parent, "Magazine", radius, 0.05f, center, Vector3.Up, material);
        for (int i = 0; i < pockets; i++)
        {
            float angle = Mathf.Tau * i / pockets;
            Cylinder(parent, "Pocket", 0.016f, 0.07f,
                center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (radius - 0.025f) + Vector3.Down * 0.06f, Vector3.Up, DarkSteel);
        }
    }

    private MeshInstance3D _coolant;

    /// <summary>Shows or hides the coolant stream at the tool.</summary>
    public void SetCoolant(bool on)
    {
        if (_coolant == null)
        {
            if (!on)
                return;
            _coolant = Cylinder(_spindle, "Coolant", 0.004f, 0.12f, new Vector3(0.03f, -0.06f, 0f), new Vector3(-0.35f, 1f, 0f), CoolantLook());
        }
        _coolant.Visible = on;
    }

    /// <summary>Shows the edge finder's tip jumping sideways on contact.</summary>
    public void SetToolKick(bool kicked)
    {
        _toolNode.Position = new Vector3(kicked ? 0.002f : 0f, _toolDrop, 0f);
    }

    public void SetPose(AxisVector position)
    {
        foreach (var (axis, node) in _links)
            node.Transform = MachineKinematics.LinkTransform(axis, position.Get(axis.Letter));
    }

    /// <summary>Shows tool `number` of the spec in the spindle.</summary>
    public void SetTool(int number) => ShowTool(_spec.FindTool(number));

    /// <summary>Shows this tool in the spindle (null = empty spindle),
    /// sticking out lengthErrorMm more than nominal. The nominal tip stays
    /// at the position LinuxCNC reports, so a longer tool lifts the head.</summary>
    public void ShowTool(ToolSpec tool, float lengthErrorMm = 0f)
    {
        float drop = -lengthErrorMm * M;
        if (_toolShown && tool == _shownTool && Mathf.IsEqualApprox(drop, _toolDrop))
            return;
        _toolShown = true;
        _shownTool = tool;
        _toolDrop = drop;
        Clear(_toolNode);
        _toolNode.Position = new Vector3(0f, drop, 0f);
        if (tool != null)
            _toolNode.AddChild(ToolMeshBuilder.MillTool(tool));

        float noseY = (tool?.LengthMm * M ?? _longestTool) + ToolMeshBuilder.HolderLength;
        _spindle.Position = new Vector3(0f, noseY, 0f);
        if (_headFollowsToolLength)
            _headMount.Position = new Vector3(0f, noseY, 0f);
    }
}
