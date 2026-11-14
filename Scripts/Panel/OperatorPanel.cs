using Godot;
using CncTrainer.Bridge;

namespace CncTrainer.Panel;

/// <summary>
/// Brief section 7/4: an ORIGINAL operator panel design that follows the
/// general logic/layout of common CNC pendant controls (mode selector, MDI
/// keypad, JOG with selectable increments, cycle start/feed hold, overrides,
/// tool-change confirm, a touch-off workflow for tool offsets) without copying
/// any real vendor's branding, logo, or exact layout. Named "Operator Panel",
/// not "Fanuc-style" or any vendor name, anywhere a user would see it.
///
/// Built programmatically (not hand-authored as a .tscn) since a control
/// layout this size is far more maintainable as code than as hand-edited
/// anchor/offset numbers in a text scene file.
/// </summary>
public partial class OperatorPanel : Control
{
    private BridgeClient _bridge;
    // Placeholder until Configure() runs; matches a lathe's real axes, same
    // as LatheMachine's own.
    private (AxisId Axis, byte Joint)[] _axes = { (AxisId.X, (byte)0), (AxisId.Z, (byte)1) };

    private Label _modeStatusLabel;
    private Label _toolChangeLabel;
    private LineEdit _mdiInput;
    private HSlider _feedOverrideSlider;
    private HSlider _spindleOverrideSlider;
    private Label _overrideLabel;
    private OptionButton _jogIncrementSelect;
    private Button _continuousJogToggle;

    private bool _continuousJogMode = true;
    private static readonly float[] IncrementSteps = { 0.001f, 0.01f, 0.1f, 1f, 10f };
    private const float JogSpeedMmPerSec = 20f;

    public override void _Ready()
    {
        // Smaller than the first pass: user feedback was "UI covers everywhere"
        // at 1080p -- a shared small Theme shrinks every child control's
        // default font uniformly instead of hand-tuning each one.
        Theme = new Theme { DefaultFontSize = 12 };

        AnchorLeft = 0;
        AnchorTop = 1;
        AnchorRight = 0;
        AnchorBottom = 1;
        OffsetLeft = 10;
        OffsetTop = -260;
        OffsetRight = 560;
        OffsetBottom = -10;

        var panelBg = new Godot.Panel { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(panelBg);
        // Row-building needs _bridge/_axes, not available until Configure()
        // runs (called from Main._Ready, after this control's own _Ready) --
        // see this class's Configure method.
    }

    /// <summary>Called by Main._Ready once this rig's BridgeClient exists --
    /// replaces the old self-lookup via the fixed "/root/BridgeClient"
    /// autoload path, which a second live machine (Phase B) can't support.</summary>
    public void Configure(MachineRig rig)
    {
        _bridge = rig.Bridge;
        _axes = rig.Profile.Axes;

        var root = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1 };
        root.AddThemeConstantOverride("separation", 2);
        AddChild(root);

        _modeStatusLabel = new Label { Text = "MODE: MANUAL" };
        root.AddChild(_modeStatusLabel);

        _toolChangeLabel = new Label { Text = "", Modulate = new Color(1f, 0.6f, 0.1f) };
        root.AddChild(_toolChangeLabel);

        root.AddChild(BuildModeRow());
        root.AddChild(BuildJogRow());
        root.AddChild(BuildMdiRow());
        root.AddChild(BuildProgramRow());
        root.AddChild(BuildCycleRow());
        root.AddChild(BuildOverridesRow());
        root.AddChild(BuildToolRow());
    }

    public override void _Process(double delta)
    {
        var s = _bridge.State;
        _modeStatusLabel.Text = $"MODE: {s.TaskMode}   INTERP: {s.InterpState}   ESTOP: {(s.Estop ? "YES" : "no")}   ON: {s.MachineOn}";
        _toolChangeLabel.Text = s.InterpState == InterpState.Waiting
            ? $"TOOL CHANGE PENDING (T{s.ToolNumber}) -- jog to clear, then press CONFIRM"
            : "";
    }

    private Control BuildModeRow()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);

        var modeRow = new HBoxContainer();
        modeRow.AddChild(MakeButton("MANUAL", () => _bridge.SetMode(TaskMode.Manual)));
        modeRow.AddChild(MakeButton("MDI", () => _bridge.SetMode(TaskMode.Mdi)));
        modeRow.AddChild(MakeButton("AUTO", () => _bridge.SetMode(TaskMode.Auto)));
        modeRow.AddChild(MakeButton("ON", () => { _bridge.ResetEstop(); _bridge.MachineOn(); }));
        modeRow.AddChild(MakeButton("E-STOP", () => _bridge.MachineOff()));
        col.AddChild(modeRow);

        var homeRow = new HBoxContainer();
        foreach (var (axis, joint) in _axes)
            homeRow.AddChild(MakeButton($"HOME {axis}", () => _bridge.HomeAxis(joint)));
        homeRow.AddChild(MakeButton("HOME ALL", () => _bridge.HomeAxis(0xFF)));
        col.AddChild(homeRow);

        return col;
    }

    private Control BuildJogRow()
    {
        var row = new HBoxContainer();

        _continuousJogToggle = MakeButton("JOG:CONT", () =>
        {
            _continuousJogMode = !_continuousJogMode;
            _continuousJogToggle.Text = _continuousJogMode ? "JOG:CONT" : "JOG:INC";
        });
        row.AddChild(_continuousJogToggle);

        _jogIncrementSelect = new OptionButton();
        foreach (var step in IncrementSteps)
            _jogIncrementSelect.AddItem($"{step} mm");
        _jogIncrementSelect.Select(2); // 0.1mm default, a common manual-mode step
        row.AddChild(_jogIncrementSelect);

        foreach (var (axis, joint) in _axes)
        {
            row.AddChild(MakeJogButton($"{axis}-", axis: joint, direction: -1));
            row.AddChild(MakeJogButton($"{axis}+", axis: joint, direction: +1));
        }
        return row;
    }

    private Button MakeJogButton(string text, byte axis, int direction)
    {
        var button = MakeButton(text, () => { });
        button.ButtonDown += () =>
        {
            if (_continuousJogMode)
            {
                _bridge.JogStart(axis, direction * JogSpeedMmPerSec);
            }
            else
            {
                float step = IncrementSteps[_jogIncrementSelect.Selected];
                _bridge.JogIncremental(axis, direction * JogSpeedMmPerSec, direction * step);
            }
        };
        button.ButtonUp += () =>
        {
            if (_continuousJogMode)
                _bridge.JogStop(axis);
        };
        return button;
    }

    private Control BuildMdiRow()
    {
        var row = new HBoxContainer();
        _mdiInput = new LineEdit { PlaceholderText = "MDI: e.g. G0 X10 Z-5", CustomMinimumSize = new Vector2(150, 0) };
        _mdiInput.TextSubmitted += _ => SendMdiFromInput();
        row.AddChild(_mdiInput);
        row.AddChild(MakeButton("EXECUTE", SendMdiFromInput));
        return row;
    }

    private void SendMdiFromInput()
    {
        var text = _mdiInput.Text.Trim();
        if (text.Length == 0)
            return;
        _bridge.SendMdi(text);
        _mdiInput.Text = "";
    }

    private Control BuildProgramRow()
    {
        var row = new HBoxContainer();
        var pathInput = new LineEdit
        {
            Text = "nc_files/demo_facing_turning.ngc", // relative to the bridge's config dir (its cwd)
            CustomMinimumSize = new Vector2(150, 0),
        };
        row.AddChild(pathInput);
        row.AddChild(MakeButton("LOAD PROGRAM", () => _bridge.OpenProgram(pathInput.Text.Trim())));
        return row;
    }

    private Control BuildCycleRow()
    {
        var row = new HBoxContainer();
        row.AddChild(MakeButton("CYCLE START", () => _bridge.RunProgram()));
        row.AddChild(MakeButton("FEED HOLD", () => _bridge.PauseProgram()));
        row.AddChild(MakeButton("STOP", () => _bridge.StopProgram()));
        row.AddChild(MakeButton("STEP", () => _bridge.StepProgram()));
        row.AddChild(MakeButton("CONFIRM TC", () => _bridge.ConfirmToolChange()));
        return row;
    }

    private Control BuildOverridesRow()
    {
        var row = new VBoxContainer();

        _overrideLabel = new Label { Text = "Feed 100%   Spindle 100%" };
        row.AddChild(_overrideLabel);

        var feedRow = new HBoxContainer();
        feedRow.AddChild(new Label { Text = "Feed Ovr" });
        _feedOverrideSlider = new HSlider { MinValue = 0, MaxValue = 150, Value = 100, Step = 5, CustomMinimumSize = new Vector2(130, 0) };
        _feedOverrideSlider.ValueChanged += v =>
        {
            _bridge.SetFeedOverride((float)(v / 100.0));
            UpdateOverrideLabel();
        };
        feedRow.AddChild(_feedOverrideSlider);
        row.AddChild(feedRow);

        var spindleRow = new HBoxContainer();
        spindleRow.AddChild(new Label { Text = "Spindle Ovr" });
        _spindleOverrideSlider = new HSlider { MinValue = 0, MaxValue = 120, Value = 100, Step = 5, CustomMinimumSize = new Vector2(130, 0) };
        _spindleOverrideSlider.ValueChanged += v =>
        {
            _bridge.SetSpindleOverride((float)(v / 100.0));
            UpdateOverrideLabel();
        };
        spindleRow.AddChild(_spindleOverrideSlider);
        row.AddChild(spindleRow);

        return row;
    }

    private void UpdateOverrideLabel()
    {
        _overrideLabel.Text = $"Feed {_feedOverrideSlider.Value:F0}%   Spindle {_spindleOverrideSlider.Value:F0}%";
    }

    private Control BuildToolRow()
    {
        // Simplified tool-offset "touch off" workflow: operator jogs the tool
        // until it touches a reference surface of a KNOWN dimension, enters
        // that known dimension, and this sends the G10 L1 offset-table-update
        // MDI line LinuxCNC expects -- the same mechanism a real pendant's
        // "touch off" soft-key drives, just without a graphical wizard.
        var row = new HBoxContainer();
        var knownDiameterInput = new LineEdit { PlaceholderText = "known diameter (mm)", CustomMinimumSize = new Vector2(100, 0) };
        row.AddChild(knownDiameterInput);
        row.AddChild(MakeButton("TOUCH OFF X", () =>
        {
            if (float.TryParse(knownDiameterInput.Text, out float knownDiameter))
            {
                float measuredRadius = _bridge.State.Position.X;
                _bridge.SendMdi($"G10 L1 P{_bridge.State.ToolNumber} X{measuredRadius - knownDiameter / 2f:F4}");
            }
        }));

        var knownZInput = new LineEdit { PlaceholderText = "known Z face offset (mm)", CustomMinimumSize = new Vector2(100, 0) };
        row.AddChild(knownZInput);
        row.AddChild(MakeButton("TOUCH OFF Z", () =>
        {
            if (float.TryParse(knownZInput.Text, out float knownZ))
            {
                float measuredZ = _bridge.State.Position.Z;
                _bridge.SendMdi($"G10 L1 P{_bridge.State.ToolNumber} Z{measuredZ - knownZ:F4}");
            }
        }));
        return row;
    }

    private Button MakeButton(string text, System.Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }
}
