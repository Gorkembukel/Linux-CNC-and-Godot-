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

    public event System.Action<IMachine> MachineSelected;

    private System.Collections.Generic.IReadOnlyList<IMachine> _machines;
    private Machining.CutMonitor _monitor;
    private VBoxContainer _layout;
    private VBoxContainer _rows;
    private OptionButton _machineSelect;
    private string _demoProgram = "";

    private Label _modeStatusLabel;
    private Label _toolChangeLabel;
    private Label _positionLabel;
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

        // Top-left, under the status text: the game often runs embedded in
        // the editor at 1:1, where the bottom of the view is off-screen.
        AnchorLeft = 0;
        AnchorTop = 0;
        AnchorRight = 0;
        AnchorBottom = 0;
        OffsetLeft = 10;
        OffsetTop = 130;
        OffsetRight = 560;
        OffsetBottom = 400;

        var panelBg = new Godot.Panel { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(panelBg);

        _layout = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1 };
        _layout.AddThemeConstantOverride("separation", 2);
        AddChild(_layout);

        var selectorRow = new HBoxContainer();
        selectorRow.AddChild(new Label { Text = "MACHINE" });
        _machineSelect = new OptionButton();
        _machineSelect.ItemSelected += index => Select((int)index);
        selectorRow.AddChild(_machineSelect);
        _layout.AddChild(selectorRow);
        // Row-building needs _bridge/_axes, not available until Configure()
        // runs (called from Main._Ready, after this control's own _Ready) --
        // see this class's Configure method.
    }

    /// <summary>Called by Main._Ready with every machine in the workshop;
    /// the MACHINE selector then rebuilds the rows below it for whichever
    /// one is picked (axes, demo program and touch-off differ per machine).</summary>
    public void SetMachines(System.Collections.Generic.IReadOnlyList<IMachine> machines)
    {
        _machines = machines;
        _machineSelect.Clear();
        foreach (var machine in machines)
            _machineSelect.AddItem(machine.Rig.Profile.Name);
        Select(0);
    }

    /// <summary>Points the panel at the machine whose console the player
    /// is standing at.</summary>
    public void SelectMachine(MachineRig rig)
    {
        for (int i = 0; i < _machines.Count; i++)
            if (_machines[i].Rig == rig && _machineSelect.Selected != i)
            {
                _machineSelect.Select(i);
                Select(i);
            }
    }

    private void Select(int index)
    {
        var machine = _machines[index];
        _bridge = machine.Rig.Bridge;
        _monitor = machine.Rig.Monitor;
        _axes = machine.Rig.Profile.Axes;
        _demoProgram = machine.Rig.Profile.DemoProgram;

        _rows?.QueueFree();
        var root = _rows = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 2);
        _layout.AddChild(root);
        MachineSelected?.Invoke(machine);

        _modeStatusLabel = new Label { Text = "MODE: MANUAL" };
        root.AddChild(_modeStatusLabel);

        _toolChangeLabel = new Label { Text = "", Modulate = new Color(1f, 0.6f, 0.1f) };
        root.AddChild(_toolChangeLabel);

        _positionLabel = new Label();
        root.AddChild(_positionLabel);

        root.AddChild(BuildModeRow());
        root.AddChild(BuildJogRow());
        root.AddChild(BuildMdiRow());
        root.AddChild(BuildProgramRow());
        root.AddChild(BuildCycleRow());
        root.AddChild(BuildOverridesRow());
        root.AddChild(BuildToolRow());
        if (System.Array.Exists(_axes, a => a.Axis == AxisId.Y))
            root.AddChild(BuildWorkZeroRow());
    }

    public override void _Process(double delta)
    {
        if (_bridge == null)
            return;
        // The rows differ per machine; size the panel to fit them.
        float bottom = OffsetTop + _layout.GetCombinedMinimumSize().Y + 6f;
        if (!Mathf.IsEqualApprox(OffsetBottom, bottom))
            OffsetBottom = bottom;

        var s = _bridge.State;
        _modeStatusLabel.Text = $"MODE: {s.TaskMode}   INTERP: {s.InterpState}   ESTOP: {(s.Estop ? "YES" : "no")}   ON: {s.MachineOn}";
        string work = "WORK", machine = "MACH";
        foreach (var (axis, _) in _axes)
        {
            work += $"  {axis}{s.WorkPosition.Get(axis):F3}";
            machine += $"  {axis}{s.Position.Get(axis):F3}";
        }
        _positionLabel.Text = work + "\n" + machine + (_monitor != null ? "\n" + _monitor.StatusLine(s.ToolNumber) : "");
        _toolChangeLabel.Text = s.ToolChangePending
            ? "TOOL CHANGE PENDING -- press CONFIRM TC"
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
            Text = _demoProgram, // relative to the bridge's config dir (its cwd)
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
        row.AddChild(MakeButton("COOLANT M8", () => _bridge.SendMdi("M8")));
        row.AddChild(MakeButton("M9", () => _bridge.SendMdi("M9")));
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
        if (System.Array.Exists(_axes, a => a.Axis == AxisId.Y))
            return BuildToolLengthRow(row);
        var knownDiameterInput = new LineEdit { PlaceholderText = "known diameter (mm)", CustomMinimumSize = new Vector2(100, 0) };
        row.AddChild(knownDiameterInput);
        row.AddChild(MakeButton("TOUCH OFF X", () =>
        {
            if (OffsetCommands.TryParse(knownDiameterInput.Text, out float knownDiameter))
                OffsetCommands.TouchOffDiameter(this, _bridge, knownDiameter);
        }));

        var knownZInput = new LineEdit { PlaceholderText = "known Z face offset (mm)", CustomMinimumSize = new Vector2(100, 0) };
        row.AddChild(knownZInput);
        row.AddChild(MakeButton("TOUCH OFF Z", () =>
        {
            if (OffsetCommands.TryParse(knownZInput.Text, out float knownZ))
                OffsetCommands.TouchOffLatheZ(this, _bridge, knownZ);
        }));
        return row;
    }

    /// <summary>Work zero: with the tool (or edge finder) at a known spot
    /// of the part, type that spot's coordinate and SET writes the G54
    /// offset so the current position reads as that value.</summary>
    private Control BuildWorkZeroRow()
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "WORK ZERO G54" });
        var valueInput = new LineEdit { Text = "0", TooltipText = "Coordinate of the spot the tool is at now", CustomMinimumSize = new Vector2(70, 0) };
        row.AddChild(valueInput);
        foreach (var axis in new[] { AxisId.X, AxisId.Y, AxisId.Z })
            row.AddChild(MakeButton("SET " + axis, () =>
            {
                if (OffsetCommands.TryParse(valueInput.Text, out float value))
                    OffsetCommands.SetWorkZero(_bridge, axis, value);
            }));
        return row;
    }

    private Control BuildToolLengthRow(HBoxContainer row)
    {
        var knownZInput = new LineEdit { PlaceholderText = "known surface Z (mm)", CustomMinimumSize = new Vector2(120, 0) };
        row.AddChild(knownZInput);
        row.AddChild(MakeButton("TOUCH OFF Z", () =>
        {
            if (OffsetCommands.TryParse(knownZInput.Text, out float knownZ))
                OffsetCommands.TouchOffToolLength(this, _bridge, knownZ);
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
