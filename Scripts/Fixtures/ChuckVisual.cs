using System.Collections.Generic;
using Godot;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>
/// A chuck drawn from its own sizes, standing on its back face: origin at
/// the centre of the back, +Y out through the jaw face. The back plate with
/// its cam studs, the body with a groove (jaw slot) per jaw, the scroll's
/// thread showing at the bottom of each groove, key sockets round the rim,
/// slot numbers; or a collet chuck's taper nose and nut. Jaws sit in their
/// grooves where the grip puts them. Visual only: the machine, the bench or
/// the hand that holds it decides what it does.
/// </summary>
public partial class ChuckVisual : Node3D
{
    private const float M = WorldScale.MmToM;

    public ChuckModel Model { get; private set; }
    private float _r, _body, _grooveWidth, _jawHeight;
    private readonly List<Node3D> _jawHolders = new();
    /// <summary>Each groove's visible scroll thread: a pool of ridges moved
    /// as the scroll turns.</summary>
    private Node3D _scroll;
    private float _grooveInner, _grooveOuter, _pitch;
    private readonly List<JawKind?> _jawKinds = new();
    private Node3D _collet;

    /// <summary>Jaw face height above the back (m): the chuck's length.</summary>
    public float LengthM => Model.LengthMm * M;
    /// <summary>The body's front face above the back (m).</summary>
    public float FaceM => _body;
    public float RadiusM => _r;
    public int Slots => _jawHolders.Count;

    public static ChuckVisual Create(ChuckModel model)
    {
        var visual = new ChuckVisual { Name = "ChuckVisual" };
        visual.Build(model);
        return visual;
    }

    /// <summary>Unit vector out along a slot's groove (this frame).</summary>
    public Vector3 SlotDirection(int slot)
    {
        float a = Mathf.Tau * slot / Mathf.Max(1, _jawHolders.Count);
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    /// <summary>Where a jaw is laid into a slot's groove on the bench: its
    /// gripping face so far in that the whole jaw lies on the face (this frame).</summary>
    public Vector3 GrooveEntry(int slot) => SlotDirection(slot) * (_r - JawLengthM - 0.002f) + Vector3.Up * _body;

    public float JawLengthM => _r * 0.45f;

    private void Build(ChuckModel model)
    {
        Model = model;
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _jawHolders.Clear();
        _jawKinds.Clear();
        _scroll = null;
        _r = model.DiameterMm / 2f * M;
        _jawHeight = model.JawDepthMm * 0.7f * M;
        _body = model.BodyLengthMm * M;
        float back = Mathf.Min(0.012f, _body * 0.2f);

        // Back plate and the cam studs that go into the spindle nose.
        BodyParts.Cylinder(this, "BackPlate", _r * 0.8f, back, new Vector3(0f, back / 2f, 0f), Vector3.Up, BodyParts.DarkSteel);
        for (int i = 0; i < model.CamCount; i++)
        {
            float a = Mathf.Tau * (i + 0.5f) / model.CamCount;
            BodyParts.Cylinder(this, "CamStud", 0.006f, 0.014f, new Vector3(Mathf.Cos(a) * _r * 0.55f, -0.006f, Mathf.Sin(a) * _r * 0.55f), Vector3.Up, BodyParts.Steel);
        }
        if (model.Kind == ChuckKind.Collet)
        {
            BuildCollet(back);
            return;
        }

        var steel = ChuckSteel;
        var dark = BodyParts.Mat(new Color(0.07f, 0.07f, 0.08f), 0.6f, 0.55f);
        // Bright ground thread, so it reads against the dark slot walls.
        var scroll = BodyParts.Mat(new Color(0.86f, 0.84f, 0.79f), 0.95f, 0.2f);
        scroll.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

        int slots = Mathf.Max(2, model.JawCount);
        _grooveWidth = _r * 0.24f;
        float inner = model.BoreMm / 2f * M + 0.004f, outer = _r - 0.004f, length = outer - inner;
        float pitch = model.ScrollPitchMm * M;
        _grooveInner = inner;
        _grooveOuter = outer;
        _pitch = pitch;
        // Slots are T-shaped: a narrow mouth the jaw's tongue slides in, a
        // wider undercut below, and the scroll (or the jaw screw) at the bottom.
        float mouth = Mathf.Min(0.007f, (_body - back) * 0.15f), under = Mathf.Min(0.012f, (_body - back) * 0.25f);
        float floor = _body - mouth - under;

        // The body, with the slots and the bore cut out of it.
        var body = new CsgCombiner3D { Name = "Body" };
        AddChild(body);
        body.AddChild(new CsgCylinder3D
        {
            Radius = _r, Height = _body - back, Sides = 64, Material = steel,
            Position = new Vector3(0f, back + (_body - back) / 2f, 0f),
        });
        body.AddChild(new CsgCylinder3D
        {
            Operation = CsgShape3D.OperationEnum.Subtraction, Radius = model.BoreMm / 2f * M, Height = _body * 2f, Sides = 32,
            Material = dark, Position = new Vector3(0f, _body / 2f, 0f),
        });
        for (int i = 0; i < slots; i++)
        {
            float a = Mathf.Tau * i / slots;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var basis = SlotBasis(a);
            float mid = inner + (length + 0.01f) / 2f;
            body.AddChild(new CsgBox3D
            {
                Operation = CsgShape3D.OperationEnum.Subtraction, Size = new Vector3(length + 0.01f, mouth + 0.002f, _grooveWidth),
                Material = dark, Transform = new Transform3D(basis, dir * mid + Vector3.Up * (_body - mouth / 2f + 0.001f)),
            });
            body.AddChild(new CsgBox3D
            {
                Operation = CsgShape3D.OperationEnum.Subtraction, Size = new Vector3(length + 0.01f, under, _grooveWidth * 1.5f),
                Material = dark, Transform = new Transform3D(basis, dir * mid + Vector3.Up * (floor + under / 2f)),
            });
        }

        if (model.Kind == ChuckKind.ThreeJaw)
        {
            // The scroll: a disc with one spiral thread on its face, turned
            // by the key; seen down the slots its ridges march in or out.
            _scroll = new Node3D { Name = "Scroll", Position = Vector3.Up * floor };
            AddChild(_scroll);
            BodyParts.Cylinder(_scroll, "Disc", outer, 0.002f, new Vector3(0f, 0.001f, 0f), Vector3.Up, BodyParts.Mat(new Color(0.45f, 0.45f, 0.47f), 0.8f, 0.35f));
            _scroll.AddChild(new MeshInstance3D { Name = "Thread", Mesh = SpiralThread(inner, outer, pitch, Mathf.Min(0.004f, under * 0.45f)), MaterialOverride = scroll, Position = Vector3.Up * 0.002f });
        }

        for (int i = 0; i < slots; i++)
        {
            float a = Mathf.Tau * i / slots;
            var groove = new Node3D { Name = $"Groove{i + 1}", Basis = SlotBasis(a), Position = Vector3.Up * _body };
            AddChild(groove);
            // A four-jaw chuck's jaw screw runs along the bottom of its slot.
            if (model.Kind != ChuckKind.ThreeJaw)
                BodyParts.Cylinder(groove, "Screw", _grooveWidth * 0.28f, length * 0.9f, new Vector3(inner + length / 2f, -mouth - under * 0.6f, 0f), Vector3.Right, scroll);
            groove.AddChild(new Label3D
            {
                Text = (i + 1).ToString(), FontSize = 64, PixelSize = _r * 0.0012f, Modulate = new Color(0.1f, 0.1f, 0.1f),
                Position = new Vector3(outer - 0.008f, 0.0005f, _grooveWidth / 2f + _r * 0.06f), RotationDegrees = new Vector3(-90f, 0f, 0f),
            });
            // The key socket on the rim between this slot and the next (a
            // four-jaw chuck: the jaw's own screw head at the slot's end).
            float socketAngle = model.Kind == ChuckKind.ThreeJaw ? a + Mathf.Pi / slots : a;
            var socketDir = new Vector3(Mathf.Cos(socketAngle), 0f, Mathf.Sin(socketAngle));
            var socket = new Node3D { Name = $"KeySocket{i + 1}", Basis = SlotBasis(socketAngle), Position = socketDir * _r + Vector3.Up * (_body * 0.6f) };
            AddChild(socket);
            BodyParts.Box(socket, "Square", new Vector3(0.004f, 0.011f, 0.011f), Vector3.Zero, dark);

            var holder = new Node3D { Name = $"Jaw{i + 1}", Basis = SlotBasis(a), Visible = false };
            AddChild(holder);
            _jawHolders.Add(holder);
            _jawKinds.Add(null);
        }
    }

    /// <summary>One spiral thread from the outer edge inward, a pitch per
    /// turn, as a raised ridge with flanks: at angle a (this frame) its
    /// ridges sit at outer - pitch * ((-a / 2pi) mod 1 + k).</summary>
    private static ArrayMesh SpiralThread(float inner, float outer, float pitch, float height)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float turns = (outer - inner) / pitch, half = pitch * 0.22f;
        int steps = Mathf.CeilToInt(turns * 96f);
        Vector3 At(float u, float dr, float y)
        {
            float a = -Mathf.Tau * u, r = outer - pitch * u + dr;
            return new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        }
        for (int i = 0; i < steps; i++)
        {
            float u0 = turns * i / steps, u1 = turns * (i + 1) / steps;
            // Top, outer flank, inner flank (sloped: a ground thread form).
            Quad(At(u0, -half * 0.5f, height), At(u1, -half * 0.5f, height), At(u1, half * 0.5f, height), At(u0, half * 0.5f, height));
            Quad(At(u0, half * 0.5f, height), At(u1, half * 0.5f, height), At(u1, half, 0f), At(u0, half, 0f));
            Quad(At(u0, -half, 0f), At(u1, -half, 0f), At(u1, -half * 0.5f, height), At(u0, -half * 0.5f, height));
        }
        // The thread's start at the outer end, a flank of its own.
        Quad(At(0f, -half, 0f), At(0f, -half * 0.5f, height), At(0f, half * 0.5f, height), At(0f, half, 0f));
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>Ground chuck body steel: lighter and shinier than machine castings.</summary>
    public static readonly StandardMaterial3D ChuckSteel = BodyParts.Mat(new Color(0.66f, 0.67f, 0.7f), 0.85f, 0.3f);

    private void BuildCollet(float back)
    {
        var steel = ChuckSteel;
        float noseLength = _jawHeight;
        BodyParts.Cylinder(this, "Body", _r, _body - back, new Vector3(0f, back + (_body - back) / 2f, 0f), Vector3.Up, steel);
        BodyParts.Cylinder(this, "Taper", _r * 0.62f, noseLength * 0.5f, new Vector3(0f, _body + noseLength * 0.25f, 0f), Vector3.Up, steel, topRadius: _r * 0.5f);
        // The collet nut: hexagon flats and two spanner holes.
        var nut = new MeshInstance3D
        {
            Name = "Nut",
            Mesh = new CylinderMesh { TopRadius = _r * 0.5f, BottomRadius = _r * 0.5f, Height = noseLength * 0.5f, RadialSegments = 6 },
            MaterialOverride = BodyParts.DarkSteel,
            Position = new Vector3(0f, _body + noseLength * 0.75f, 0f),
        };
        AddChild(nut);
        foreach (float side in new[] { -1f, 1f })
            BodyParts.Box(this, "SpannerHole", new Vector3(0.006f, 0.006f, 0.006f), new Vector3(side * _r * 0.5f, _body + noseLength * 0.75f, 0f),
                BodyParts.Mat(new Color(0.05f, 0.05f, 0.05f)));
        _collet = new Node3D { Name = "Collet", Visible = false };
        AddChild(_collet);
        BodyParts.Cylinder(_collet, "Collet", Model.MaxOpeningMm / 2f * M + 0.004f, 0.004f, new Vector3(0f, _body + noseLength + 0.001f, 0f), Vector3.Up,
            BodyParts.Mat(new Color(0.7f, 0.66f, 0.45f), 0.8f, 0.3f));
    }

    /// <summary>A slot's frame: X out along the groove, Y up out of the face.</summary>
    private static Basis SlotBasis(float angle)
    {
        var x = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        return new Basis(x, Vector3.Up, x.Cross(Vector3.Up));
    }

    /// <summary>The scroll seen down each groove: its ridges march in as
    /// the key closes it, and a fresh ridge appears at a groove's outer end
    /// the moment the thread's start passes that slot: the sign to lay
    /// that slot's jaw in.</summary>
    public void ShowScroll(float scrollTurns)
    {
        // Turned so that at slot i the ridges sit at
        // outer - pitch * (frac(turns - i/N) + k), as ChuckJaws counts them.
        if (_scroll != null)
            _scroll.Rotation = new Vector3(0f, -Mathf.Tau * scrollTurns, 0f);
    }

    /// <summary>Puts the jaws where the grip has them: in their slots at
    /// the opening (a jaw caught out of turn further out), or out.</summary>
    public void ShowGrip(ChuckGrip grip)
    {
        ShowScroll(grip.Jaws.ScrollTurns);
        if (_collet != null)
        {
            _collet.Visible = grip.Jaws.ColletMm != null;
            return;
        }
        float jawLength = JawLengthM;
        for (int i = 0; i < _jawHolders.Count && i < grip.Jaws.SlotCount; i++)
        {
            var holder = _jawHolders[i];
            var seated = grip.Jaws.Slot(i) ?? grip.Jaws.Waiting(i);
            holder.Visible = seated != null;
            if (seated is not SeatedJaw jaw)
                continue;
            if (_jawKinds[i] != jaw.Kind)
            {
                foreach (Node child in holder.GetChildren())
                    child.QueueFree();
                JawMesh.Build(holder, jaw.Kind, jaw.Number, jawLength, _jawHeight, _grooveWidth);
                _jawKinds[i] = jaw.Kind;
            }
            bool waiting = grip.Jaws.Slot(i) == null;
            // Caught while the others are still going in, a jaw comes in
            // with the scroll from where it was caught.
            float r = waiting ? _r - jawLength
                : !grip.Jaws.Complete ? _r - jawLength - _pitch * (grip.Jaws.Notch - jaw.CaughtNotch) / (float)(_jawHolders.Count * ChuckJaws.NotchesPerSlot)
                : (grip.Inside ? grip.OpeningMm / 2f * M - jawLength : grip.OpeningMm / 2f * M) + grip.Jaws.ErrorMm(i) * M;
            r = Mathf.Clamp(r, Model.BoreMm / 2f * M, _r - jawLength);
            holder.Position = SlotDirection(i) * r + Vector3.Up * _body;
        }
    }
}

/// <summary>A chuck jaw drawn in its slot's frame: X out along the groove
/// from the gripping face, Y up out of the chuck face, the guide tongue
/// below the face. Hard jaws are stepped (reversed: the steps the other
/// way round), soft jaws a plain block.</summary>
public static class JawMesh
{
    public static void Build(Node3D parent, JawKind kind, int number, float length, float height, float width)
    {
        var material = BodyParts.Mat(JawItem.ColorOf(kind), kind == JawKind.Soft ? 0.5f : 0.75f, kind == JawKind.Soft ? 0.45f : 0.35f);
        var teeth = BodyParts.Mat(new Color(0.12f, 0.12f, 0.13f), 0.6f, 0.5f);
        float w = width * 0.92f;
        // Guide tongue in the groove, with the scroll teeth under it.
        BodyParts.Box(parent, "Tongue", new Vector3(length, 0.004f, w * 0.7f), new Vector3(length / 2f, -0.0015f, 0f), material);
        for (int t = 0; t < 4; t++)
            BodyParts.Box(parent, "Tooth", new Vector3(length * 0.08f, 0.0015f, w * 0.65f), new Vector3(length * (0.15f + 0.22f * t), -0.004f, 0f), teeth);
        if (kind == JawKind.Soft)
            BodyParts.Box(parent, "Block", new Vector3(length, height, w), new Vector3(length / 2f, height / 2f, 0f), material);
        else
        {
            // Three steps: tallest at the gripping face for hard jaws, at
            // the outer end for reversed ones.
            for (int s = 0; s < 3; s++)
            {
                int along = kind == JawKind.HardReversed ? 2 - s : s;
                float h = height * (1f - 0.3f * s);
                BodyParts.Box(parent, $"Step{s + 1}", new Vector3(length / 3f, h, w), new Vector3(length * (along + 0.5f) / 3f, h / 2f, 0f), material);
            }
            // Serrations on the gripping face.
            float face = kind == JawKind.HardReversed ? length : 0f;
            for (int g = 0; g < 4; g++)
                BodyParts.Box(parent, "Serration", new Vector3(0.0008f, 0.0012f, w), new Vector3(face, height * (0.2f + 0.2f * g), 0f), teeth);
        }
        parent.AddChild(new Label3D
        {
            Text = number.ToString(), FontSize = 48, PixelSize = width * 0.004f,
            Position = new Vector3(length * 0.8f, height * (kind == JawKind.Soft ? 1f : 0.4f) + 0.0006f, 0f),
            RotationDegrees = new Vector3(-90f, 90f, 0f),
            Modulate = kind == JawKind.Soft ? Colors.Black : Colors.White,
        });
    }
}
