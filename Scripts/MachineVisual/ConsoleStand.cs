using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Player;

namespace CncTrainer;

/// <summary>
/// The machine's control pendant, modelled on a 0i-F Plus style panel: LCD
/// with softkeys and MDI keypad on top, machine operator panel below (mode
/// keys, jog cross, overrides, spindle keys, cycle start / feed hold,
/// emergency stop, handwheel). Every key is a real StaticBody3D the player's
/// crosshair presses through TryPress; dials and the handwheel are
/// ConsoleKnobs turned with the mouse wheel.
///
/// Layout coordinates are pixels measured on a reference photo of a real
/// panel (At() converts them), so proportions follow the real thing.
/// [Tool]: the geometry previews in the editor; live data only at runtime.
/// </summary>
[Tool]
public partial class ConsoleStand : Node3D
{
    [Export]
    public float PostHeight
    {
        get => _postHeight;
        set { _postHeight = value; RebuildIfInTree(); }
    }

    [Export]
    public float PanelTiltDegrees
    {
        get => _panelTiltDegrees;
        set { _panelTiltDegrees = value; RebuildIfInTree(); }
    }

    /// <summary>Text on the yellow strip above the screen; empty = a generic
    /// name chosen by machine type.</summary>
    [Export]
    public string BrandText
    {
        get => _brandText;
        set { _brandText = value; RebuildIfInTree(); }
    }

    private float _postHeight = 1.1f;
    private float _panelTiltDegrees = -8f;
    private string _brandText = "";

    private const float Px = 0.000513f;
    private const float CenterPx = 717f, CenterPy = 1040f;
    private const float FaceSize = 0.60f;
    private const float RapidMmPerMin = 5000f, IncMmPerMin = 1000f, HandleMmPerMin = 2000f;
    private static readonly float[] JogFeedMmPerMin = { 0, 2, 3.2f, 5, 7.9f, 12.6f, 20, 32, 50, 79, 126, 200, 320, 500, 790, 1260 };

    private static readonly Color Housing = Hex("2B2E33"), Bezel = Hex("3A3D42"), MopPlate = Hex("2F3237");
    private static readonly Color KeyGrey = Hex("45494F"), KeyLight = Hex("D0D3D4"), Silkscreen = Hex("E4E6E8");
    private static readonly Color Yellow = Hex("FFD100"), LampGreen = Hex("7CFF4F"), LampAmber = Hex("FFB020"), LampRed = Hex("FF3B30");

    private enum Mode { Edit, Auto, Mdi, Handle, Jog, Inc, Ref }
    private enum Page { Pos, Prog, Offset, System, Message, Custom, Help }

    private sealed record KeyEntry(Node3D Node, float BaseZ, Action Action);
    private sealed record Lamp(StandardMaterial3D Material, Func<bool> On, bool Blink, Color Color, bool Cap);

    public MachineRig Rig { get; private set; }

    private BridgeClient _bridge;
    private MachineSafety _safety;
    private (AxisId Axis, byte Joint)[] _axes = { (AxisId.X, (byte)0), (AxisId.Z, (byte)1) };
    private string _program = "";

    private ConsoleScreen _screen;
    private Label3D _toolDisplay;
    private ConsoleKnob _feedKnob, _spindleKnob, _mpgAxisKnob;
    private readonly Dictionary<ulong, KeyEntry> _keys = new();
    private readonly List<Lamp> _lamps = new();

    private Mode _mode = Mode.Jog;
    private Page _page = Page.Pos;
    private int _subPage;
    private bool _singleBlock, _rapid, _shift, _overridesSynced;
    private int _step = 2;
    private int _jogHeldJoint = -1;
    private float _spindleRpm = 500f;
    private string _keyIn = "";
    private readonly List<string> _mdiBlocks = new();
    private readonly List<string> _programLines = new();
    private bool _programEdited;
    private int _progCursor, _dirCursor, _offsetCursor;
    private string[] _dirFiles = Array.Empty<string>();
    private readonly List<string> _alarms = new();
    private readonly List<string> _messages = new();

    private bool IsLathe => !_axes.Any(a => a.Axis == AxisId.Y);
    private MachineState S => _bridge.State;

    private void RebuildIfInTree()
    {
        if (IsInsideTree())
            RebuildGeometry();
    }

    public override void _Ready()
    {
        if (!Engine.IsEditorHint())
            AddToGroup("console_stands");
        RebuildGeometry();
    }

    /// <summary>Called by the machine once its BridgeClient exists; rebuilds
    /// so the jog keys and lamps match this machine's axes.</summary>
    public void Configure(MachineRig rig)
    {
        Rig = rig;
        _bridge = rig.Bridge;
        _axes = rig.Profile.Axes;
        LoadProgram(rig.Profile.DemoProgram);
        _bridge.EventReceived += OnBridgeEvent;
        if (GetParent() is ProceduralMachine machine && machine.Safety != null)
        {
            _safety = machine.Safety;
            _safety.Tripped += incident => AddLine(_alarms, incident.Text.Replace('\n', ' '));
        }
        RebuildGeometry();
    }

    private void OnBridgeEvent(int kind, string text)
    {
        if ((EventKind)kind == EventKind.Alarm)
            AddLine(_alarms, text);
        else
            AddLine(_messages, text);
    }

    private static void AddLine(List<string> list, string text)
    {
        list.Insert(0, $"{DateTime.Now:HH:mm:ss} {text}");
        if (list.Count > 8)
            list.RemoveAt(list.Count - 1);
    }

    // ------------------------------------------------------------------ geometry

    private static Vector3 At(float px, float py, float z = 0f) => new((px - CenterPx) * Px, (CenterPy - py) * Px, z);
    private static Vector2 PxSize(float w, float h) => new(w * Px, h * Px);

    private void RebuildGeometry()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _keys.Clear();
        _lamps.Clear();

        var post = new StaticBody3D { Name = "Post", Position = new Vector3(0f, PostHeight / 2f, 0f) };
        AddChild(post);
        var postMat = Mat(Hex("5A5E64"), 0.3f, 0.6f);
        post.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, PostHeight, 0.12f) }, MaterialOverride = postMat });
        post.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.12f, PostHeight, 0.12f) } });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.02f, 0.4f) }, MaterialOverride = postMat, Position = new Vector3(0f, 0.01f, 0f) });

        var face = new Node3D
        {
            Name = "Face",
            Position = new Vector3(0f, PostHeight + FaceSize / 2f + 0.03f, 0.07f),
            RotationDegrees = new Vector3(PanelTiltDegrees, 0f, 0f),
        };
        AddChild(face);

        var housing = new StaticBody3D { Name = "Housing" };
        face.AddChild(housing);
        var shellSize = new Vector3(FaceSize + 0.04f, FaceSize + 0.04f, 0.13f);
        housing.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = shellSize }, MaterialOverride = Mat(Hex("C9CCCF"), 0.1f, 0.6f), Position = new Vector3(0f, 0f, -0.07f) });
        housing.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(FaceSize, FaceSize, 0.01f) }, MaterialOverride = Mat(Housing, 0.1f, 0.7f), Position = new Vector3(0f, 0f, -0.004f) });
        housing.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = shellSize }, Position = new Vector3(0f, 0f, -0.07f) });

        BuildCncUnit(face);
        BuildMop(face);
    }

    private void BuildCncUnit(Node3D face)
    {
        const float top = 0.010f;
        Plate(face, 210, 520, 990, 950, top, Bezel);
        Plate(face, 1015, 520, 1225, 950, 0.004f, Hex("26282C"));
        foreach (var (x, y) in new[] { (1028f, 533f), (1212f, 533f), (1028f, 937f), (1212f, 937f) })
            Disc(face, At(x, y, 0.004f), 0.003f, 0.002f, Mat(Hex("8A8E94"), 0.8f, 0.3f));
        Plate(face, 216, 600, 266, 770, top + 0.001f, Hex("2A2C30"));
        Plate(face, 216, 785, 266, 845, top + 0.001f, Hex("2A2C30"));

        Plate(face, 290, 538, 710, 553, top + 0.0005f, Yellow);
        string brand = _brandText.Length > 0 ? _brandText : IsLathe ? "CNC TRAINER  Series 1i-T Plus" : "CNC TRAINER  Series 1i-M Plus";
        Text(face, brand, At(700, 546, top + 0.0012f), 0.0042f, Hex("202020"), HorizontalAlignment.Right);

        BuildScreen(face, top);

        var softMat = Mat(Hex("2C2E33"), 0.1f, 0.5f);
        for (int i = 0; i < ConsoleScreen.SoftkeyCount; i++)
        {
            int index = i;
            AddKey(face, At(290 + (i + 0.5f) * 42f, 916, top), PxSize(30, 18), 0.004f, softMat, "", 0f, () => Softkey(index)).Name = $"Softkey{i}";
        }
        AddKey(face, At(262, 916, top), PxSize(22, 18), 0.004f, softMat, "<", 0.004f, () => _subPage = 0);
        AddKey(face, At(738, 916, top), PxSize(22, 18), 0.004f, softMat, ">", 0.004f, () => _subPage = (_subPage + 1) % ViewCount());
        for (int i = 0; i < 8; i++)
            AddKey(face, At(737, 600 + i * 38f, top), PxSize(22, 26), 0.004f, softMat, "", 0f, null);

        BuildKeypad(face, top);
    }

    private void BuildScreen(Node3D face, float top)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(ConsoleScreen.Width, ConsoleScreen.Height),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Disable3D = true,
        };
        AddChild(viewport);
        _screen = new ConsoleScreen();
        viewport.AddChild(_screen);

        var lip = Mat(Hex("1E2024"), 0.1f, 0.5f);
        Plate(face, 284, 564, 716, 570, top + 0.002f, lip);
        Plate(face, 284, 890, 716, 896, top + 0.002f, lip);
        Plate(face, 284, 570, 290, 890, top + 0.002f, lip);
        Plate(face, 710, 570, 716, 890, top + 0.002f, lip);
        face.AddChild(new MeshInstance3D
        {
            Name = "Screen",
            Mesh = new QuadMesh { Size = PxSize(420, 320) },
            MaterialOverride = new StandardMaterial3D { AlbedoTexture = viewport.GetTexture(), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            Position = At(500, 730, top + 0.0006f),
        });
    }

    private void BuildKeypad(Node3D face, float top)
    {
        var keyMat = Mat(KeyGrey, 0.1f, 0.55f);
        string[,] main =
        {
            { "O", "N", "G", "7", "8", "9" },
            { "X", "Y", "Z", "4", "5", "6" },
            { "M", "S", "T", "1", "2", "3" },
            { "F", "H", "EOB", "-", "0", "." },
        };
        string[,] shifted =
        {
            { "P", "Q", "R", "A", "B", "C" },
            { "U", "V", "W", "[", "]", " " },
            { "I", "J", "K", ",", "#", "=" },
            { "L", "D", "E", "+", "*", "/" },
        };
        float[] xs = { 773, 808, 843, 878, 912, 946 };
        float[] ys = { 600, 634, 669, 703 };
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 6; c++)
            {
                string m = main[r, c], sh = shifted[r, c];
                string sub = sh == " " ? "SP" : sh;
                AddKey(face, At(xs[c], ys[r], top), PxSize(30, 28), 0.006f, keyMat, m, m.Length > 1 ? 0.0028f : 0.0055f,
                    () => TypeKey(m == "EOB" ? ";" : m, sh), sub: sub);
            }

        (string label, Action action)[] row1 =
        {
            ("POS", () => OpenPage(Page.Pos)), ("PROG", () => OpenPage(Page.Prog)), ("OFS/SET", () => OpenPage(Page.Offset)),
            ("SHIFT", () => _shift = !_shift), ("CAN", Cancel), ("INPUT", InputKey),
        };
        (string label, Action action)[] row2 =
        {
            ("SYSTEM", () => OpenPage(Page.System)), ("MESSAGE", () => OpenPage(Page.Message)), ("CSTM/GR", () => OpenPage(Page.Custom)),
            ("ALTER", AlterKey), ("INSERT", InsertKey), ("DELETE", DeleteKey),
        };
        for (int c = 0; c < 6; c++)
        {
            AddKey(face, At(xs[c], 746, top), PxSize(30, 28), 0.006f, keyMat, row1[c].label, 0.0024f, row1[c].action,
                lamp: row1[c].label == "SHIFT" ? () => _shift : null);
            AddKey(face, At(xs[c], 780, top), PxSize(30, 28), 0.006f, keyMat, row2[c].label, 0.0022f, row2[c].action);
        }

        AddKey(face, At(791, 822, top), PxSize(30, 28), 0.006f, keyMat, "PAGE\n^", 0.0024f, () => CursorKey(-10));
        AddKey(face, At(791, 857, top), PxSize(30, 28), 0.006f, keyMat, "PAGE\nv", 0.0024f, () => CursorKey(10));
        AddKey(face, At(826, 840, top), PxSize(28, 26), 0.006f, keyMat, "<", 0.005f, () => CursorKey(-1));
        AddKey(face, At(860, 822, top), PxSize(28, 26), 0.006f, keyMat, "^", 0.005f, () => CursorKey(-1));
        AddKey(face, At(860, 857, top), PxSize(28, 26), 0.006f, keyMat, "v", 0.005f, () => CursorKey(1));
        AddKey(face, At(895, 840, top), PxSize(28, 26), 0.006f, keyMat, ">", 0.005f, () => CursorKey(1));
        AddKey(face, At(946, 822, top), PxSize(30, 28), 0.006f, keyMat, "HELP", 0.0026f, () => OpenPage(Page.Help));
        AddKey(face, At(946, 857, top), PxSize(30, 28), 0.006f, Mat(KeyLight, 0.1f, 0.5f), "RESET", 0.0024f, ResetKey, legendColor: Hex("303030"));
    }

    private void BuildMop(Node3D face)
    {
        const float top = 0.003f;
        Plate(face, 210, 995, 1225, 1560, top, MopPlate);
        var line = Mat(Hex("C8CCD0"), 0f, 0.6f);
        Frame(face, 225, 1010, 435, 1545, top, line);
        Frame(face, 437, 1010, 972, 1545, top, line);
        Plate(face, 437, 1212, 972, 1214, top + 0.0004f, line);
        Frame(face, 1003, 1010, 1208, 1545, top, line);

        BuildEstop(face, top);
        BuildJog(face, top);
        BuildOverrides(face, top);
        BuildKeyBlocks(face, top);
        BuildPowerAndHandle(face, top);
    }

    private void BuildEstop(Node3D face, float top)
    {
        Disc(face, At(325, 1160, top), 50 * Px, 0.003f, Mat(Yellow, 0.1f, 0.5f));
        Text(face, "EMERGENCY STOP", At(325, 1094, top + 0.001f), 0.0036f, Silkscreen);
        var body = new StaticBody3D { Name = "EmergencyStop", Position = At(325, 1160, top + 0.003f) };
        face.AddChild(body);
        var red = Mat(Hex("D3222A"), 0.1f, 0.35f);
        body.AddChild(Cylinder(14 * Px, 0.012f, Mat(Hex("1C1C1C"), 0.2f, 0.5f), 0.006f));
        body.AddChild(Cylinder(38 * Px, 0.010f, red, 0.017f));
        body.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 38 * Px, Height = 0.012f, IsHemisphere = true }, MaterialOverride = red, RotationDegrees = new Vector3(90f, 0f, 0f), Position = new Vector3(0f, 0f, 0.022f) });
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 38 * Px, Height = 0.03f }, RotationDegrees = new Vector3(90f, 0f, 0f), Position = new Vector3(0f, 0f, 0.015f) });
        Register(body, () => { _bridge.StopProgram(); _bridge.MachineOff(); });
    }

    private void BuildJog(Node3D face, float top)
    {
        var rotary = _axes.Where(a => a.Axis >= AxisId.A).Select(a => a.Axis).ToList();
        AxisId Rot(int i) => i < rotary.Count ? rotary[i] : (AxisId)(-1);
        (string label, AxisId axis, int dir)?[,] grid = IsLathe
            ? new (string, AxisId, int)?[,]
            {
                { null, ("-X", AxisId.X, -1), null },
                { ("-Z", AxisId.Z, -1), ("RAPID", AxisId.X, 0), ("+Z", AxisId.Z, 1) },
                { null, ("+X", AxisId.X, 1), null },
            }
            : new (string, AxisId, int)?[,]
            {
                { ("+4", Rot(0), 1), ("+Y", AxisId.Y, 1), ("-Z", AxisId.Z, -1) },
                { ("+X", AxisId.X, 1), ("RAPID", AxisId.X, 0), ("-X", AxisId.X, -1) },
                { ("-4", Rot(0), -1), ("-Y", AxisId.Y, -1), ("+Z", AxisId.Z, 1) },
            };
        float[] xs = { 272, 331, 390 };
        float[] ys = { 1272, 1316, 1361 };
        var keyMat = Mat(KeyGrey, 0.1f, 0.55f);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                if (grid[r, c] is not { } slot)
                    continue;
                var (label, axis, dir) = slot;
                if (dir == 0)
                    AddKey(face, At(xs[c], ys[r], top), PxSize(42, 34), 0.007f, keyMat, "RAPID", 0.0028f, () => _rapid = !_rapid, lamp: () => _rapid);
                else
                    AddKey(face, At(xs[c], ys[r], top), PxSize(42, 34), 0.007f, keyMat, label, 0.0055f, () => JogKey(axis, dir));
            }

        bool fifth = rotary.Count > 1;
        if (fifth)
        {
            AddKey(face, At(272, 1405, top), PxSize(42, 30), 0.007f, keyMat, "+5", 0.0055f, () => JogKey(rotary[1], 1));
            AddKey(face, At(390, 1405, top), PxSize(42, 30), 0.007f, keyMat, "-5", 0.0055f, () => JogKey(rotary[1], -1));
        }
        Plate(face, 245, 1235, 418, 1237, top + 0.0004f, Mat(Hex("C8CCD0"), 0f, 0.6f));
        Text(face, "JOG", At(331, 1225, top + 0.001f), 0.0034f, Silkscreen);

        float labelY = fifth ? 1450 : 1418;
        Text(face, "CYCLE START", At(275, labelY, top + 0.001f), 0.0034f, Silkscreen);
        Text(face, "FEED HOLD", At(382, labelY, top + 0.001f), 0.0034f, Silkscreen);
        RoundButton(face, "CycleStart", At(275, 1495, top), 22 * Px, Hex("ECECEC"), LampGreen, () => S.InCycle && S.InterpState != InterpState.Paused, CycleStart);
        RoundButton(face, "FeedHold", At(382, 1495, top), 22 * Px, Hex("1A1A1A"), LampAmber, () => S.InterpState == InterpState.Paused, () => _bridge.PauseProgram());
    }

    private void BuildOverrides(Node3D face, float top)
    {
        var feedScale = new string[16];
        for (int i = 0; i < 16; i++)
            feedScale[i] = i % 5 == 0 ? (i * 10).ToString() : "";
        _feedKnob = AddKnob(face, At(510, 1130, top), 24 * Px, 16, -135f, 18f, "FEED OVERRIDE %", feedScale, 52 * Px);
        _feedKnob.SetIndex(10);
        _feedKnob.Changed = (i, _) => _bridge?.SetFeedOverride(i * 0.1f);
        Text(face, "FEEDRATE OVERRIDE %", At(510, 1040, top + 0.001f), 0.0032f, Silkscreen);
        Text(face, "JOG mm/min", At(510, 1200, top + 0.001f), 0.0026f, Silkscreen);

        var spindleScale = Enumerable.Range(0, 8).Select(i => (50 + i * 10).ToString()).ToArray();
        _spindleKnob = AddKnob(face, At(637, 1130, top), 24 * Px, 8, -105f, 30f, "SPINDLE OVERRIDE %", spindleScale, 50 * Px);
        _spindleKnob.SetIndex(5);
        _spindleKnob.Changed = (i, _) => _bridge?.SetSpindleOverride(0.5f + i * 0.1f);
        Text(face, "SPINDLE OVERRIDE %", At(637, 1040, top + 0.001f), 0.0032f, Silkscreen);

        var line = Mat(Hex("C8CCD0"), 0f, 0.6f);
        Frame(face, 700, 1050, 875, 1190, top, line);
        Text(face, "TOOL No.", At(787, 1070, top + 0.001f), 0.0032f, Silkscreen);
        Plate(face, 790, 1095, 878, 1145, top + 0.001f, Hex("080808"));
        _toolDisplay = Text(face, "000", At(834, 1120, top + 0.0025f), 0.011f, LampGreen, font: Mono());
        Text(face, "SPINDLE", At(745, 1165, top + 0.001f), 0.0028f, Silkscreen);
        AddLamp(face, At(720, 1120, top + 0.001f), 0.0035f, LampGreen, () => S.SpindleEnabled);

        Disc(face, At(925, 1078, top), 14 * Px, 0.008f, Mat(Hex("B8BCC2"), 0.9f, 0.25f));
        Plate(face, 923, 1068, 927, 1088, top + 0.0085f, Hex("202020"));
        Text(face, "PROTECT", At(925, 1050, top + 0.001f), 0.0026f, Silkscreen);
    }

    private void BuildKeyBlocks(Node3D face, float top)
    {
        float[] left = { 483, 541, 599, 657 };
        float[] right = { 750, 808, 866, 925 };

        KeyRow(face, top, left, 1270, new (string, Action, Func<bool>)[]
        {
            ("AUTO", () => SelectMode(Mode.Auto), () => _mode == Mode.Auto),
            ("SINGLE\nBLOCK", () => _singleBlock = !_singleBlock, () => _singleBlock),
            ("HANDLE", () => SelectMode(Mode.Handle), () => _mode == Mode.Handle),
            ("REF\nRETURN", () => SelectMode(Mode.Ref), () => _mode == Mode.Ref),
        });
        KeyRow(face, top, left, 1315, new (string, Action, Func<bool>)[]
        {
            ("EDIT", () => SelectMode(Mode.Edit), () => _mode == Mode.Edit),
            ("MDI", () => SelectMode(Mode.Mdi), () => _mode == Mode.Mdi),
            ("JOG", () => SelectMode(Mode.Jog), () => _mode == Mode.Jog),
            ("INC\nJOG", () => SelectMode(Mode.Inc), () => _mode == Mode.Inc),
        });
        Text(face, "MODE", At(570, 1240, top + 0.001f), 0.0034f, Silkscreen);

        KeyRow(face, top, right, 1270, new (string, Action, Func<bool>)[]
        {
            ("SPDL\n-10%", () => NudgeSpindle(-1), null),
            ("SPDL\n100%", () => NudgeSpindle(0), () => _spindleKnob.Index == 5),
            ("SPDL\n+10%", () => NudgeSpindle(1), null),
            ("TOOL\nCHG END", () => _bridge.ConfirmToolChange(), () => S.ToolChangePending),
        });
        KeyRow(face, top, right, 1315, new (string, Action, Func<bool>)[]
        {
            ("SPDL\nCW", () => ManualSpindle(1), () => S.SpindleEnabled && S.SpindleRpm >= 0),
            ("SPDL\nSTOP", () => ManualSpindle(0), () => !S.SpindleEnabled),
            ("SPDL\nCCW", () => ManualSpindle(-1), () => S.SpindleEnabled && S.SpindleRpm < 0),
            ("COOLANT", ToggleCoolant, () => S.Coolant),
        });
        Text(face, "SPINDLE", At(837, 1240, top + 0.001f), 0.0034f, Silkscreen);

        Action notWired = () => PlayerController.Notify(this, "Bu fonksiyon simülatörde henüz bağlı değil");
        Action builderKey = () => PlayerController.Notify(this, "Makine üreticisi tuşu: bu makinede atanmamış");
        KeyRow(face, top, left, 1408, new (string, Action, Func<bool>)[]
        {
            ("ALL\nREF", HomeAll, () => _axes.All(a => S.IsHomed(a.Axis))),
            ("BLOCK\nSKIP", notWired, null), ("OPT\nSTOP", notWired, null), ("DRY\nRUN", notWired, null),
        });
        KeyRow(face, top, left, 1452, new (string, Action, Func<bool>)[]
        {
            ("MACH\nLOCK", notWired, null), ("Z\nLOCK", notWired, null), ("MST\nLOCK", notWired, null), ("PRG\nRESTART", notWired, null),
        });
        string[] steps = { "x1\nF0", "x10\n25%", "x100\n50%", "x1000\n100%" };
        KeyRow(face, top, left, 1497, Enumerable.Range(0, 4)
            .Select(i => (steps[i], (Action)(() => _step = i), (Func<bool>)(() => _step == i))).ToArray());
        Text(face, "PROGRAM", At(570, 1378, top + 0.001f), 0.0034f, Silkscreen);
        Text(face, "INC STEP / RAPID OVR", At(570, 1532, top + 0.001f), 0.0028f, Silkscreen);

        KeyRow(face, top, right, 1408, new (string, Action, Func<bool>)[]
        {
            ("F1", builderKey, null), ("F2", builderKey, null), ("F3", builderKey, null),
            ("ALARM\nRESET", () => _alarms.Clear(), () => _alarms.Count > 0),
        });
        KeyRow(face, top, right, 1452, new (string, Action, Func<bool>)[]
        {
            ("WORK\nLIGHT", notWired, null), ("CHIP\nCONV", notWired, null), ("AIR\nBLOW", notWired, null), ("AUTO\nPWR OFF", notWired, null),
        });
        KeyRow(face, top, right, 1497, new (string, Action, Func<bool>)[]
        {
            ("F4", builderKey, null), ("F5", builderKey, null), ("F6", builderKey, null), ("F7", builderKey, null),
        });
        Text(face, "AUX", At(837, 1378, top + 0.001f), 0.0034f, Silkscreen);
    }

    private void KeyRow(Node3D face, float top, float[] xs, float y, (string label, Action action, Func<bool> lamp)[] keys)
    {
        var keyMat = Mat(KeyGrey, 0.1f, 0.55f);
        for (int i = 0; i < keys.Length; i++)
            AddKey(face, At(xs[i], y, top), PxSize(44, 36), 0.007f, keyMat, keys[i].label, 0.0026f, keys[i].action, lamp: keys[i].lamp);
    }

    private void BuildPowerAndHandle(Node3D face, float top)
    {
        Text(face, "POWER", At(1105, 1032, top + 0.001f), 0.0034f, Silkscreen);
        var on = AddKey(face, At(1053, 1075, top), PxSize(50, 38), 0.009f, Mat(Hex("F2F2F2"), 0.1f, 0.4f), "", 0f,
            () => { _bridge.ResetEstop(); _bridge.MachineOn(); });
        on.Name = "PowerOn";
        AddLamp(on, new Vector3(0f, 0f, 0.0094f), 0.006f, LampGreen, () => S.MachineOn);
        AddKey(face, At(1160, 1075, top), PxSize(50, 38), 0.009f, Mat(Hex("151515"), 0.1f, 0.4f), "", 0f, () => _bridge.MachineOff()).Name = "PowerOff";
        Text(face, "ON", At(1053, 1108, top + 0.001f), 0.0036f, Silkscreen);
        Text(face, "OFF", At(1160, 1108, top + 0.001f), 0.0036f, Silkscreen);

        Text(face, "REF POINT", At(1105, 1140, top + 0.001f), 0.0032f, Silkscreen);
        for (int i = 0; i < _axes.Length; i++)
        {
            var axis = _axes[i].Axis;
            float x = 1041 + (i % 4) * 43f, y = 1165 + (i / 4) * 48f;
            AddLamp(face, At(x, y, top + 0.001f), 0.004f, LampGreen, () => S.IsHomed(axis));
            Text(face, axis.ToString(), At(x, y + 20, top + 0.001f), 0.0034f, Silkscreen);
        }
        Text(face, "ALM", At(1170, 1265, top + 0.001f), 0.0032f, Silkscreen);
        AddLamp(face, At(1170, 1245, top + 0.001f), 0.004f, LampRed, () => S.Estop || _alarms.Count > 0, blink: true);

        var axisNames = new[] { "OFF" }.Concat(_axes.Select(a => a.Axis.ToString())).ToArray();
        _mpgAxisKnob = AddKnob(face, At(1045, 1330, top), 13 * Px, axisNames.Length, -75f, 30f, "El çarkı ekseni", axisNames, 30 * Px, wrapOnClick: true);
        Text(face, "AXIS", At(1045, 1290, top + 0.001f), 0.003f, Silkscreen);

        var handle = AddKnob(face, At(1120, 1440, top), 68 * Px, 1, 0f, 3.6f, "El çarkı (HANDLE modunda)", null, 0f, handwheel: true);
        handle.Endless = true;
        handle.Changed = (_, steps) => HandwheelTurned(steps);
        Text(face, "HANDLE", At(1120, 1360, top + 0.001f), 0.0032f, Silkscreen);
    }

    // ------------------------------------------------------------------ building blocks

    private StaticBody3D AddKey(Node3D parent, Vector3 center, Vector2 size, float depth, StandardMaterial3D material,
        string legend, float legendHeight, Action action, Func<bool> lamp = null, string sub = null, Color? legendColor = null)
    {
        var body = new StaticBody3D { Position = center };
        parent.AddChild(body);
        var box = new Vector3(size.X, size.Y, depth);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = box }, MaterialOverride = material, Position = new Vector3(0f, 0f, depth / 2f) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box }, Position = new Vector3(0f, 0f, depth / 2f) });
        var color = legendColor ?? Silkscreen;
        if (legend.Length > 0)
        {
            var offset = sub != null ? new Vector3(-size.X * 0.14f, size.Y * 0.1f, 0f) : Vector3.Zero;
            Text(body, legend, offset + new Vector3(0f, 0f, depth + 0.0004f), legendHeight, color);
        }
        if (sub != null)
            Text(body, sub, new Vector3(size.X * 0.27f, -size.Y * 0.27f, depth + 0.0004f), 0.0026f, color);
        if (lamp != null)
            AddLamp(body, new Vector3(-size.X * 0.36f, size.Y * 0.34f, depth + 0.0003f), 0.0018f, LampGreen, lamp);
        Register(body, action);
        return body;
    }

    private void RoundButton(Node3D parent, string name, Vector3 center, float radius, Color color, Color glow, Func<bool> lit, Action action)
    {
        Disc(parent, center, radius * 1.35f, 0.004f, Mat(Hex("9A9EA4"), 0.8f, 0.3f));
        var body = new StaticBody3D { Name = name, Position = center + new Vector3(0f, 0f, 0.004f) };
        parent.AddChild(body);
        var cap = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.35f, EmissionEnabled = true, Emission = glow, EmissionEnergyMultiplier = 0f };
        body.AddChild(Cylinder(radius, 0.01f, cap, 0.005f));
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = 0.01f }, RotationDegrees = new Vector3(90f, 0f, 0f), Position = new Vector3(0f, 0f, 0.005f) });
        _lamps.Add(new Lamp(cap, lit, false, glow, true));
        Register(body, action);
    }

    private ConsoleKnob AddKnob(Node3D parent, Vector3 center, float radius, int count, float start, float step,
        string caption, string[] scale, float scaleRadius, bool wrapOnClick = false, bool handwheel = false)
    {
        var knob = new ConsoleKnob { Position = center, Count = count, StartDegrees = start, DegreesPerStep = step, Caption = caption, WrapOnClick = wrapOnClick };
        parent.AddChild(knob);
        var rotor = new Node3D();
        knob.AddChild(rotor);
        knob.Rotor = rotor;
        float height = handwheel ? 0.014f : 0.018f;
        knob.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = height }, RotationDegrees = new Vector3(90f, 0f, 0f), Position = new Vector3(0f, 0f, height / 2f) });

        if (handwheel)
        {
            knob.AddChild(Cylinder(radius * 1.08f, 0.006f, Mat(Hex("1A1A1A"), 0.2f, 0.6f), 0.003f));
            rotor.AddChild(Cylinder(radius, height, Mat(Hex("202225"), 0.3f, 0.5f), height / 2f));
            rotor.AddChild(Cylinder(radius * 0.78f, 0.002f, Mat(Hex("C4C8CE"), 0.9f, 0.25f), height + 0.001f));
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.DegToRad(i * 36f);
                var dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                Text(rotor, (i * 10).ToString(), dir * radius * 0.62f + new Vector3(0f, 0f, height + 0.0025f), 0.0032f, Hex("202020"));
            }
            for (int i = 0; i < 50; i++)
            {
                float a = Mathf.DegToRad(i * 7.2f);
                rotor.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.0006f, i % 5 == 0 ? 0.004f : 0.0022f, 0.0005f) },
                    MaterialOverride = Mat(Hex("202020"), 0f, 0.6f),
                    Position = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * radius * 0.74f + new Vector3(0f, 0f, height + 0.0021f),
                    RotationDegrees = new Vector3(0f, 0f, -i * 7.2f),
                });
            }
            rotor.AddChild(Cylinder(radius * 0.13f, 0.016f, Mat(Hex("1A1A1A"), 0.2f, 0.4f), height + 0.008f, new Vector3(0f, radius * 0.3f, 0f)));
            return knob;
        }

        knob.AddChild(Cylinder(radius * 1.35f, 0.004f, Mat(Hex("A8ACB2"), 0.9f, 0.3f), 0.002f));
        rotor.AddChild(Cylinder(radius, height, Mat(Hex("1C1D20"), 0.2f, 0.5f), height / 2f));
        rotor.AddChild(Cylinder(radius * 0.8f, 0.002f, Mat(Hex("B8BCC2"), 0.9f, 0.25f), height + 0.001f));
        rotor.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.0018f, radius * 0.9f, 0.001f) },
            MaterialOverride = Mat(Hex("1A1A1A"), 0f, 0.5f),
            Position = new Vector3(0f, radius * 0.4f, height + 0.0025f),
        });
        rotor.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.0022f, radius * 0.35f, 0.0012f) },
            MaterialOverride = Mat(Silkscreen, 0f, 0.5f),
            Position = new Vector3(0f, radius * 1.15f, 0.0035f),
        });

        var tickMat = Mat(Silkscreen, 0f, 0.6f);
        for (int i = 0; i < count; i++)
        {
            float deg = start + i * step;
            float a = Mathf.DegToRad(deg);
            var dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.0007f, 0.0035f, 0.0004f) },
                MaterialOverride = tickMat,
                Position = center + dir * radius * 1.55f + new Vector3(0f, 0f, 0.0004f),
                RotationDegrees = new Vector3(0f, 0f, -deg),
            });
            if (scale != null && scale[i].Length > 0)
                Text(parent, scale[i], center + dir * scaleRadius + new Vector3(0f, 0f, 0.0006f), 0.0028f, Silkscreen);
        }
        return knob;
    }

    private void AddLamp(Node3D parent, Vector3 position, float radius, Color color, Func<bool> on, bool blink = false)
    {
        var mat = new StandardMaterial3D { AlbedoColor = color * 0.25f, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0f, Roughness = 0.3f };
        parent.AddChild(Cylinder(radius, 0.0015f, mat, 0.0008f, new Vector3(position.X, position.Y, position.Z)));
        _lamps.Add(new Lamp(mat, on, blink, color, false));
    }

    private void Register(StaticBody3D body, Action action)
    {
        _keys[body.GetInstanceId()] = new KeyEntry(body, body.Position.Z, action);
    }

    private static MeshInstance3D Cylinder(float radius, float height, StandardMaterial3D material, float z, Vector3? at = null) => new()
    {
        Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 32 },
        MaterialOverride = material,
        RotationDegrees = new Vector3(90f, 0f, 0f),
        Position = (at ?? Vector3.Zero) with { Z = z + (at?.Z ?? 0f) },
    };

    private static void Disc(Node3D parent, Vector3 center, float radius, float height, StandardMaterial3D material) =>
        parent.AddChild(Cylinder(radius, height, material, height / 2f, center));

    private static void Plate(Node3D parent, float x0, float y0, float x1, float y1, float height, Color color) =>
        Plate(parent, x0, y0, x1, y1, height, Mat(color, 0.1f, 0.6f));

    private static void Plate(Node3D parent, float x0, float y0, float x1, float y1, float height, StandardMaterial3D material) =>
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3((x1 - x0) * Px, (y1 - y0) * Px, height) },
            MaterialOverride = material,
            Position = At((x0 + x1) / 2f, (y0 + y1) / 2f, height / 2f),
        });

    private static void Frame(Node3D parent, float x0, float y0, float x1, float y1, float z, StandardMaterial3D material)
    {
        const float w = 2.5f;
        Plate(parent, x0, y0, x1, y0 + w, z + 0.0004f, material);
        Plate(parent, x0, y1 - w, x1, y1, z + 0.0004f, material);
        Plate(parent, x0, y0, x0 + w, y1, z + 0.0004f, material);
        Plate(parent, x1 - w, y0, x1, y1, z + 0.0004f, material);
    }

    private static Label3D Text(Node3D parent, string text, Vector3 position, float capHeight, Color color,
        HorizontalAlignment align = HorizontalAlignment.Center, Font font = null)
    {
        var label = new Label3D
        {
            Text = text,
            Position = position,
            FontSize = 48,
            PixelSize = capHeight / 34f,
            Modulate = color,
            OutlineSize = 0,
            HorizontalAlignment = align,
            Shaded = false,
            DoubleSided = false,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (font != null)
            label.Font = font;
        parent.AddChild(label);
        return label;
    }

    private static Font Mono() => new SystemFont { FontNames = new[] { "Consolas", "Lucida Console", "DejaVu Sans Mono", "Courier New", "monospace" } };

    private static StandardMaterial3D Mat(Color color, float metallic, float rough) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = rough,
    };

    private static Color Hex(string hex) => Color.FromHtml(hex);

    // ------------------------------------------------------------------ operation

    public Transform3D FaceTransform => GetNode<Node3D>("Face").GlobalTransform;

    /// <summary>Camera pose straight in front of the panel at the distance
    /// where the whole face fills a view of `fovDegrees` (vertical).</summary>
    public Transform3D FocusTransform(float fovDegrees, float aspect)
    {
        var face = GetNode<Node3D>("Face").GlobalTransform;
        float half = (FaceSize + 0.04f) / 2f * 1.04f;
        float tan = Mathf.Tan(Mathf.DegToRad(fovDegrees / 2f));
        float distance = Mathf.Max(half / tan, half / (tan * aspect));
        var normal = face.Basis.Z.Normalized();
        var position = face.Origin + normal * distance;
        return new Transform3D(Basis.LookingAt(-normal, face.Basis.Y.Normalized()), position);
    }

    /// <summary>Called by PlayerController when its crosshair ray hits a
    /// node; presses it if it is one of this console's keys.</summary>
    public bool TryPress(Node3D hit)
    {
        if (hit == null || !_keys.TryGetValue(hit.GetInstanceId(), out var key))
            return false;
        var tween = key.Node.CreateTween();
        tween.TweenProperty(key.Node, "position:z", key.BaseZ - 0.002f, 0.05);
        tween.TweenProperty(key.Node, "position:z", key.BaseZ, 0.08);
        if (_bridge != null)
            key.Action?.Invoke();
        return true;
    }

    private void SelectMode(Mode mode)
    {
        _mode = mode;
        _bridge.SetMode(mode switch { Mode.Auto => TaskMode.Auto, Mode.Mdi => TaskMode.Mdi, _ => TaskMode.Manual });
        if (mode is Mode.Mdi or Mode.Edit)
        {
            OpenPage(Page.Prog);
            _subPage = mode == Mode.Mdi ? 2 : 0;
        }
    }

    private bool ManualMode => _mode is Mode.Jog or Mode.Inc or Mode.Handle or Mode.Ref or Mode.Edit;

    private void EnsureManual()
    {
        if (S.TaskMode != TaskMode.Manual)
            _bridge.SetMode(TaskMode.Manual);
    }

    private bool TryJoint(AxisId axis, out byte joint)
    {
        foreach (var (a, j) in _axes)
            if (a == axis)
            {
                joint = j;
                return true;
            }
        joint = 0;
        return false;
    }

    private float StepMm => 0.001f * Mathf.Pow(10f, _step);

    private void JogKey(AxisId axis, int dir)
    {
        if (!TryJoint(axis, out byte joint))
            return;
        switch (_mode)
        {
            case Mode.Ref:
                EnsureManual();
                _bridge.HomeAxis(joint);
                break;
            case Mode.Jog:
                float feed = _rapid ? RapidMmPerMin : JogFeedMmPerMin[_feedKnob.Index];
                if (feed <= 0f)
                    return;
                EnsureManual();
                _bridge.JogStart(joint, dir * feed / 60f);
                _jogHeldJoint = joint;
                break;
            case Mode.Inc:
                EnsureManual();
                _bridge.JogIncremental(joint, dir * IncMmPerMin / 60f, dir * StepMm);
                break;
            default:
                PlayerController.Notify(this, "Eksen tuşları JOG, INC JOG ve REF RETURN modlarında çalışır");
                break;
        }
    }

    private void HandwheelTurned(int steps)
    {
        if (_bridge == null || _mode != Mode.Handle || _mpgAxisKnob.Index == 0)
            return;
        var joint = _axes[_mpgAxisKnob.Index - 1].Joint;
        float step = 0.001f * Mathf.Pow(10f, Mathf.Min(_step, 2));
        EnsureManual();
        _bridge.JogIncremental(joint, Mathf.Sign(steps) * HandleMmPerMin / 60f, Mathf.Abs(steps) * step);
    }

    private void HomeAll()
    {
        if (_mode != Mode.Ref)
        {
            PlayerController.Notify(this, "ALL REF için önce REF RETURN modunu seç");
            return;
        }
        EnsureManual();
        _bridge.HomeAxis(0xFF);
    }

    private void CycleStart()
    {
        var s = S;
        if (_mode == Mode.Auto)
        {
            if (s.InterpState == InterpState.Paused)
            {
                if (_singleBlock) _bridge.StepProgram(); else _bridge.RunProgram();
                return;
            }
            if (s.InCycle)
                return;
            if (_programLines.Count == 0)
            {
                PlayerController.Notify(this, "Seçili program yok: PROG > DIR > SELECT");
                return;
            }
            _bridge.OpenProgram(ProgramToRun());
            if (_singleBlock) _bridge.StepProgram(); else _bridge.RunProgram();
        }
        else if (_mode == Mode.Mdi)
        {
            var blocks = _mdiBlocks.Append(_keyIn)
                .SelectMany(b => b.Split(';'))
                .Select(b => b.Trim())
                .Where(b => b.Length > 0)
                .ToList();
            foreach (var block in blocks)
                _bridge.SendMdi(block);
            _mdiBlocks.Clear();
            _keyIn = "";
        }
        else
        {
            PlayerController.Notify(this, "CYCLE START: AUTO veya MDI modunda çalışır");
        }
    }

    private void ManualSpindle(int dir)
    {
        if (dir == 0)
        {
            _bridge.SpindleOff();
            return;
        }
        if (!ManualMode || S.InCycle)
        {
            PlayerController.Notify(this, "İş milini elle çalıştırmak için JOG / HANDLE modunda ol");
            return;
        }
        EnsureManual();
        _bridge.SpindleOn(dir * _spindleRpm);
    }

    private void NudgeSpindle(int dir)
    {
        _spindleKnob.SetIndex(dir == 0 ? 5 : Mathf.Clamp(_spindleKnob.Index + dir, 0, 7));
        _bridge.SetSpindleOverride(0.5f + _spindleKnob.Index * 0.1f);
    }

    private void ToggleCoolant()
    {
        if (S.InCycle)
            return;
        _bridge.SendMdi(S.Coolant ? "M9" : "M8");
    }

    private void ResetKey()
    {
        _bridge.StopProgram();
        _alarms.Clear();
        _shift = false;
    }

    private void TypeKey(string main, string shifted)
    {
        _keyIn += _shift ? shifted : main;
        _shift = false;
    }

    private void Cancel()
    {
        if (_keyIn.Length > 0)
            _keyIn = _keyIn[..^1];
    }

    private bool EditingProgram => _page == Page.Prog && _subPage == 0 && _mode != Mode.Mdi;

    private string[] KeyInBlocks() =>
        _keyIn.Split(';').Select(b => b.Trim()).Where(b => b.Length > 0).ToArray();

    private bool CanEditProgram()
    {
        if (_mode == Mode.Edit && !S.InCycle)
            return true;
        PlayerController.Notify(this, "Programı değiştirmek için EDIT modunu seç (makine dururken)");
        return false;
    }

    private void InputKey()
    {
        if (_page == Page.Offset)
            OffsetEntry(measure: false);
        else if (_page == Page.Prog && _subPage == 1)
            SelectProgram();
        else
            InsertKey();
    }

    private void InsertKey()
    {
        var blocks = KeyInBlocks();
        if (blocks.Length == 0)
            return;
        if (EditingProgram)
        {
            if (!CanEditProgram())
                return;
            _programLines.InsertRange(Mathf.Min(_progCursor + 1, _programLines.Count), blocks);
            _progCursor = Mathf.Min(_progCursor + blocks.Length, _programLines.Count - 1);
            _programEdited = true;
        }
        else
        {
            _mdiBlocks.AddRange(blocks);
            _page = Page.Prog;
            _subPage = 2;
            PlayerController.Notify(this, _mode == Mode.Mdi
                ? "Blok MDI programına eklendi: çalıştırmak için CYCLE START"
                : "Blok MDI programına eklendi: MDI modunu seç, sonra CYCLE START");
        }
        _keyIn = "";
    }

    private void AlterKey()
    {
        var blocks = KeyInBlocks();
        if (blocks.Length == 0)
            return;
        if (EditingProgram)
        {
            if (!CanEditProgram() || _programLines.Count == 0)
                return;
            _programLines[_progCursor] = string.Join(" ", blocks);
            _programEdited = true;
        }
        else if (_mdiBlocks.Count > 0)
        {
            _mdiBlocks[^1] = string.Join(" ", blocks);
        }
        _keyIn = "";
    }

    private void DeleteKey()
    {
        if (_keyIn.Length > 0)
        {
            _keyIn = "";
            return;
        }
        if (EditingProgram)
        {
            if (!CanEditProgram() || _programLines.Count == 0)
                return;
            _programLines.RemoveAt(_progCursor);
            _progCursor = Mathf.Clamp(_progCursor, 0, Mathf.Max(_programLines.Count - 1, 0));
            _programEdited = true;
        }
        else
        {
            _mdiBlocks.Clear();
        }
    }

    private void CursorKey(int amount)
    {
        switch (_page)
        {
            case Page.Prog when _subPage == 0:
                _progCursor = Mathf.Clamp(_progCursor + amount, 0, Mathf.Max(_programLines.Count - 1, 0));
                break;
            case Page.Prog when _subPage == 1:
                _dirCursor = Mathf.Clamp(_dirCursor + amount, 0, Mathf.Max(_dirFiles.Length - 1, 0));
                break;
            case Page.Offset:
                _offsetCursor = Mathf.Clamp(_offsetCursor + Math.Sign(amount), 0, OffsetAxes().Length - 1);
                break;
        }
    }

    private void OpenPage(Page page)
    {
        _page = page;
        _subPage = page == Page.Prog && _mode == Mode.Mdi ? 2 : 0;
        _offsetCursor = 0;
    }

    private int ViewCount() => _page switch { Page.Pos or Page.Prog => 3, Page.Offset or Page.Message => 2, _ => 1 };

    private (string Label, Action Act)[] Softkeys()
    {
        Action View(int i) => () => _subPage = i;
        return _page switch
        {
            Page.Pos => new (string, Action)[] { ("ABS", View(0)), ("MACHINE", View(1)), ("ALL", View(2)) },
            Page.Prog => new (string, Action)[]
            {
                ("PROGRAM", View(0)), ("DIR", OpenDir), ("MDI", View(2)), ("", null),
                _subPage == 1 ? ("SELECT", (Action)SelectProgram) : _subPage == 0 && _programEdited ? ("REVERT", (Action)RevertProgram) : ("", (Action)null),
            },
            Page.Offset => new (string, Action)[]
            {
                ("TOOL", View(0)), ("WORK", View(1)), ("", null), ("", null),
                ("MEASURE", () => OffsetEntry(measure: true)), ("INPUT", () => OffsetEntry(measure: false)),
            },
            Page.Message => new (string, Action)[] { ("ALARM", View(0)), ("MSG", View(1)) },
            Page.System => new (string, Action)[] { ("LINK", null) },
            Page.Custom => new (string, Action)[] { ("LOAD", null) },
            _ => new (string, Action)[] { ("HELP", null) },
        };
    }

    private string[] SoftkeyTexts()
    {
        var keys = Softkeys();
        return Enumerable.Range(0, ConsoleScreen.SoftkeyCount).Select(i => i < keys.Length ? keys[i].Label : "").ToArray();
    }

    private void Softkey(int index)
    {
        var keys = Softkeys();
        if (index >= 0 && index < keys.Length)
            keys[index].Act?.Invoke();
    }

    // ------------------------------------------------------------------ programs

    private static string FullPath(string program) => ProjectSettings.GlobalizePath("res://linuxcnc-config/" + program);

    private void LoadProgram(string program)
    {
        _program = program;
        _programLines.Clear();
        if (program.Length > 0 && System.IO.File.Exists(FullPath(program)))
            _programLines.AddRange(System.IO.File.ReadAllLines(FullPath(program)));
        _programEdited = false;
        _progCursor = 0;
    }

    private void RevertProgram() => LoadProgram(_program);

    private void OpenDir()
    {
        _subPage = 1;
        string dir = ProjectSettings.GlobalizePath("res://linuxcnc-config/nc_files");
        _dirFiles = System.IO.Directory.Exists(dir)
            ? System.IO.Directory.GetFiles(dir, "*.ngc").Select(System.IO.Path.GetFileName)
                .Where(n => !n.StartsWith(EditedPrefix)).OrderBy(n => n).ToArray()
            : Array.Empty<string>();
        _dirCursor = Mathf.Max(Array.IndexOf(_dirFiles, System.IO.Path.GetFileName(_program)), 0);
    }

    private void SelectProgram()
    {
        if (_dirFiles.Length == 0)
            return;
        if (S.InCycle)
        {
            PlayerController.Notify(this, "Program çalışırken başka program seçilemez");
            return;
        }
        LoadProgram("nc_files/" + _dirFiles[_dirCursor]);
        _subPage = 0;
    }

    private const string EditedPrefix = "_edit_";

    /// <summary>The program AUTO runs: the file itself, or, once it was
    /// edited on the control, a copy beside it (the original stays as it is).</summary>
    private string ProgramToRun()
    {
        if (!_programEdited)
            return _program;
        string copy = "nc_files/" + EditedPrefix + System.IO.Path.GetFileName(_program);
        System.IO.File.WriteAllLines(FullPath(copy), _programLines);
        return copy;
    }

    // ------------------------------------------------------------------ offsets

    private AxisId[] OffsetAxes() => _subPage == 0
        ? (IsLathe ? new[] { AxisId.X, AxisId.Z } : new[] { AxisId.Z })
        : _axes.Select(a => a.Axis).ToArray();

    /// <summary>MEASURE: the key-in is the known value at the current
    /// position (a diameter for lathe X). INPUT: the key-in is the offset
    /// itself. A leading axis letter in the key-in picks the row.</summary>
    private void OffsetEntry(bool measure)
    {
        var axes = OffsetAxes();
        string text = _keyIn.Trim().TrimEnd(';');
        if (text.Length > 0 && char.IsLetter(text[0]))
        {
            int row = Array.FindIndex(axes, a => a.ToString() == text[..1].ToUpperInvariant());
            if (row < 0)
                return;
            _offsetCursor = row;
            text = text[1..];
        }
        if (!OffsetCommands.TryParse(text, out float value))
        {
            PlayerController.Notify(this, "Önce değeri yaz (ör. 48 veya X48), sonra " + (measure ? "MEASURE" : "INPUT"));
            return;
        }
        if (S.InCycle)
            return;
        var axis = axes[Mathf.Clamp(_offsetCursor, 0, axes.Length - 1)];
        if (_subPage == 1)
        {
            if (measure) OffsetCommands.SetWorkZero(_bridge, axis, value);
            else OffsetCommands.SetWorkOffset(_bridge, axis, value);
        }
        else if (!measure)
            OffsetCommands.SetToolOffset(this, _bridge, axis, value);
        else if (!IsLathe)
            OffsetCommands.TouchOffToolLength(this, _bridge, value);
        else if (axis == AxisId.X)
            OffsetCommands.TouchOffDiameter(this, _bridge, value);
        else
            OffsetCommands.TouchOffLatheZ(this, _bridge, value);
        _keyIn = "";
    }

    // ------------------------------------------------------------------ per frame

    public override void _Process(double delta)
    {
        if (_bridge == null || _screen == null)
            return;
        var s = S;
        bool connected = _bridge.IsBridgeConnected;

        if (_jogHeldJoint >= 0 && !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _bridge.JogStop((byte)_jogHeldJoint);
            _jogHeldJoint = -1;
        }
        if (!_overridesSynced && connected && s.SampleCount > 0)
        {
            _feedKnob.SetIndex(Mathf.Clamp(Mathf.RoundToInt(s.FeedOverride * 10f), 0, 15));
            _spindleKnob.SetIndex(Mathf.Clamp(Mathf.RoundToInt((s.SpindleOverride - 0.5f) * 10f), 0, 7));
            _overridesSynced = true;
        }
        if (s.SpindleEnabled && Mathf.Abs((float)s.SpindleRpm) > 1f && s.SpindleOverride > 0f)
            _spindleRpm = Mathf.Abs((float)s.SpindleRpm) / s.SpindleOverride;

        bool blinkOn = Time.GetTicksMsec() / 400 % 2 == 0;
        foreach (var lamp in _lamps)
        {
            bool on = connected && lamp.On() && (!lamp.Blink || blinkOn);
            lamp.Material.EmissionEnergyMultiplier = on ? (lamp.Cap ? 0.8f : 2.5f) : 0f;
            if (!lamp.Cap)
                lamp.Material.AlbedoColor = on ? lamp.Color : lamp.Color * 0.25f;
        }
        _toolDisplay.Text = connected ? $"{s.ToolNumber:000}" : "---";

        _screen.SetSoftkeys(SoftkeyTexts());
        if (!connected)
        {
            _screen.Render("POSITION", "", "", "NO SIGNAL", _keyIn, "-- LINK DOWN --");
            return;
        }
        // The control's screen uses '.' decimals whatever the OS locale is.
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            var (title, big, body) = PageText(s);
            _screen.Render(title, $"{ProgramName()}  N{Mathf.Max(s.MotionLine, 0):00000}", big, body, _keyIn, StatusText(s, blinkOn));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    private string ProgramName()
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(_program);
        return name.Length > 16 ? name[..16] : name;
    }

    private string StatusText(MachineState s, bool blinkOn)
    {
        string mode = _mode switch
        {
            Mode.Edit => "EDIT", Mode.Auto => "MEM ", Mode.Mdi => "MDI ", Mode.Handle => "HND ",
            Mode.Jog => "JOG ", Mode.Inc => "INC ", _ => "REF ",
        };
        string state = s.InterpState == InterpState.Paused ? "HOLD" : s.InCycle ? "STRT" : "****";
        string motion = s.Velocity.Length() > 1e-4 ? "MTN" : "***";
        string alarm = s.Estop ? "-EMG-" : _alarms.Count > 0 && blinkOn ? " ALM " : "     ";
        string power = s.MachineOn ? "   " : "OFF";
        return $"{mode} {state} {motion} {power} {alarm}  {(_rapid ? "RPD" : "   ")} {(_singleBlock ? "SBK" : "   ")}   {DateTime.Now:HH:mm:ss}";
    }

    private (string title, string big, string body) PageText(MachineState s)
    {
        string feedLine = $"ACT.F {s.Velocity.Length() * 60.0,8:F0} MM/MIN   S {s.SpindleRpm,6:F0}   T{s.ToolNumber:00}";
        string ovrLine = $"FEED OVR {s.FeedOverride * 100:F0}%   SPDL OVR {s.SpindleOverride * 100:F0}%   JOG F {(_rapid ? RapidMmPerMin : JogFeedMmPerMin[_feedKnob.Index]):F0}";
        string monitor = Rig?.Monitor != null ? "\n" + Rig.Monitor.StatusLine(s.ToolNumber) : "";
        switch (_page)
        {
            case Page.Pos when _subPage == 2:
            {
                string table = "        ABSOLUTE      MACHINE\n";
                foreach (var (axis, _) in _axes)
                    table += $"  {axis}  {s.WorkPosition.Get(axis),11:F3}  {s.Position.Get(axis),11:F3}\n";
                return ("POSITION (ALL)", "", table + "\n" + feedLine + "\n" + ovrLine + monitor);
            }
            case Page.Pos:
            {
                bool machine = _subPage == 1;
                string big = string.Join("\n", _axes.Select(a => $"{a.Axis} {(machine ? s.Position : s.WorkPosition).Get(a.Axis),11:F3}"));
                return (machine ? "POSITION (MACHINE)" : "POSITION (ABSOLUTE)", big, feedLine + "\n" + ovrLine + monitor);
            }
            case Page.Prog when _subPage == 2:
            {
                string blocks = _mdiBlocks.Count == 0 ? "  (empty)" : string.Join("\n", _mdiBlocks.Select((b, i) => $"  N{(i + 1) * 10:0000} {b}"));
                return ("PROGRAM (MDI)", "", "O0000 (MDI)\n" + blocks + "\n  %\n\nMDI mode: type, INSERT, then CYCLE START.");
            }
            case Page.Prog when _subPage == 1:
            {
                int first = Mathf.Clamp(_dirCursor - 6, 0, Mathf.Max(_dirFiles.Length - 13, 0));
                var rows = _dirFiles.Skip(first).Take(13).Select((name, i) =>
                {
                    long kb = new System.IO.FileInfo(FullPath("nc_files/" + name)).Length / 1024 + 1;
                    string mark = first + i == _dirCursor ? ">" : " ";
                    string current = "nc_files/" + name == _program ? "*" : " ";
                    return $"{mark}{current} {name,-38} {kb,5}K";
                });
                return ("PROGRAM DIRECTORY", "", $"  {_dirFiles.Length} programs   cursor + SELECT (or INPUT)\n" + string.Join("\n", rows));
            }
            case Page.Prog:
            {
                int exec = s.InCycle && s.MotionLine > 0 ? s.MotionLine - 1 : -1;
                int focus = exec >= 0 ? exec : _progCursor;
                int first = Mathf.Clamp(focus - 5, 0, Mathf.Max(_programLines.Count - 12, 0));
                var rows = _programLines.Skip(first).Take(12).Select((line, i) =>
                {
                    int n = first + i;
                    string mark = n == exec ? "*" : n == _progCursor && _mode == Mode.Edit ? ">" : " ";
                    string text = line.Length > 50 ? line[..50] : line;
                    return $"{mark}{n + 1,5} {text}";
                });
                string footer = s.ToolChangePending ? "*** TOOL CHANGE: load the tool, press TOOL CHG END ***"
                    : s.InterpState == InterpState.Paused ? "FEED HOLD  (CYCLE START resumes)"
                    : s.InCycle ? "PROGRAM RUNNING"
                    : _mode == Mode.Edit ? "EDIT: cursor, then INSERT / ALTER / DELETE"
                    : "AUTO + CYCLE START runs this program";
                string header = _programLines.Count == 0 ? "(no program: PROG > DIR > SELECT)"
                    : $"{_program}  {_programLines.Count} lines{(_programEdited ? "  *EDITED*" : "")}";
                return ("PROGRAM", "", header + "\n" + string.Join("\n", rows) + "\n" + footer);
            }
            case Page.Offset:
            {
                var axes = OffsetAxes();
                bool work = _subPage == 1;
                string rows = string.Join("\n", axes.Select((axis, i) =>
                    $"{(i == _offsetCursor ? ">" : " ")} {axis}   POS {s.WorkPosition.Get(axis),10:F3}   OFS {s.WorkOffset.Get(axis),10:F3}"));
                string help = work
                    ? "MEASURE: current spot = key-in  (G10 L20)\nINPUT  : G54 offset = key-in    (G10 L2)"
                    : IsLathe
                        ? "MEASURE X: key-in = measured DIAMETER\nMEASURE Z: key-in = Z of the touched face\nINPUT  : tool offset = key-in   (G10 L1)"
                        : "MEASURE Z: key-in = Z of the touched surface\nINPUT  : tool length offset = key-in";
                string title = work ? "OFFSET (WORK G54)" : $"OFFSET (TOOL T{s.ToolNumber:00})";
                string extra = work ? "" : $"{(IsLathe ? "NOSE R" : "TOOL DIA")} {s.ToolNoseRadius:F3}{monitor}\n";
                return (title, "", extra + rows + "\n\n" + help + "\nCursor picks the axis (or type X48).");
            }            case Page.Message when _subPage == 1:
                return ("MESSAGE", "", _messages.Count == 0 ? "NO MESSAGE" : string.Join("\n", _messages));
            case Page.Message:
            {
                var lines = new List<string>();
                if (s.Estop) lines.Add("EMERGENCY STOP");
                if (!s.MachineOn) lines.Add("MACHINE OFF  (POWER ON)");
                if (s.MachineOn && !_axes.All(a => s.IsHomed(a.Axis))) lines.Add("ZERO RETURN NOT FINISHED  (REF RETURN)");
                if (s.ToolChangePending) lines.Add("TOOL CHANGE WAITING  (TOOL CHG END)");
                lines.AddRange(_alarms);
                return ("ALARM", "", lines.Count == 0 ? "NO ALARM" : string.Join("\n", lines));
            }
            case Page.System:
                return ("SYSTEM", "", $"MACHINE   {Rig?.Profile.Name}\nLINK      seq {s.LastSeq}  age {_bridge.LastPacketAgeSeconds * 1000:F0} ms\nJITTER    {_bridge.StateStreamJitterMs:F1} ms\nMODE      {s.TaskMode}\nINTERP    {s.InterpState}");
            case Page.Custom:
            {
                float load = Rig?.Monitor != null ? Rig.Monitor.Load.SpindleLoad : 0f;
                int bars = Mathf.Clamp(Mathf.RoundToInt(load * 20f), 0, 30);
                return ("LOAD METER", "", $"SPINDLE LOAD {load * 100f,4:F0}%\n[{new string('|', bars)}{new string(' ', 30 - bars)}]\n         100%^" + monitor);
            }
            default:
                return ("HELP", "", "POWER ON -> REF RETURN + ALL REF (or +axis)\nJOG: hold an axis key   INC JOG: x1..x1000\nHANDLE: pick AXIS, turn the wheel (x1..x100)\nMDI: type, INSERT, CYCLE START\nPROG > DIR: cursor + SELECT picks the program\nEDIT: cursor + INSERT / ALTER / DELETE lines\nAUTO: CYCLE START runs it (edited = a copy)\nOFS/SET: type value, MEASURE or INPUT\nRESET stops,  FEED HOLD pauses");
        }
    }
}
