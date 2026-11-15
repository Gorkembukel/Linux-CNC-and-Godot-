using Godot;
using CncTrainer.Interaction;
using CncTrainer.Workshop;

namespace CncTrainer.Player;

/// <summary>
/// First groundwork for the planned FP/TP human character that walks up to
/// and interacts with the machine (user's stated end goal). CharacterBody3D
/// with a capsule collider, walking against the floor/machine/console
/// collision added in the WorldScale refactor, mouse-look, and a V-key toggle
/// between first- and third-person camera. World units are meters
/// (WorldScale.cs) -- this is exactly the scale conversion that work set up
/// for.
///
/// Interaction, scoped for this first pass: standing within reach of the
/// console shows a prompt; pressing E toggles the mouse out of "look" capture
/// and shows/hides the 2D OperatorPanel overlay, so the player can actually
/// use the real controls while "at the console." This does not make the 3D
/// console model itself clickable (see ConsoleStand.cs's own doc comment) --
/// it's a proximity-gated shortcut to the existing, proven 2D panel, not a
/// simulation of physically pressing the 3D buttons.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    [Export] public float WalkSpeed = 2.0f; // m/s -- an unhurried human walking pace
    [Export] public float RunSpeedMultiplier = 1.8f;
    [Export] public float JumpVelocity = 4.0f;
    [Export] public float Gravity = 9.8f;
    [Export] public float MouseSensitivity = 0.0025f;
    [Export] public float InteractRange = 1.2f;
    [Export] public float PickupRange = 2.0f; // longer reach than InteractRange -- grabbing, not pressing a button at arm's length

    private const float CapsuleRadius = 0.3f;
    private const float CapsuleHeight = 1.8f;
    private const float EyeHeight = 1.65f;

    private Camera3D _fpCamera;
    private Camera3D _tpCamera;
    private SpringArm3D _tpArm;
    private Node3D _head; // pitch pivot, shared by both cameras' parent chain
    private bool _firstPerson = true;
    private float _pitch;

    private ConsoleStand _consoleStand;
    private Label _interactPrompt;
    private Control _operatorPanel;
    private bool _atConsole;
    private Node3D _bodyVisual;

    private Node3D _carryPoint;
    private RigidBody3D _heldPart;
    private StockStation _station;
    private bool _atStation;
    private Label _heldLabel;

    /// <summary>Where the crosshair ray last hit something (global).</summary>
    public Vector3 LookPoint { get; private set; }

    private Label _noticeLabel;
    private Label _statusLabel;
    private Label _sheetLabel;

    /// <summary>A live reading from whatever the player is measuring with;
    /// set every frame by its source, shown under the crosshair.</summary>
    public string ToolStatus;
    private double _noticeTime;

    /// <summary>Shows a warning in the middle of the player's screen.</summary>
    public static void Notify(Node from, string text)
    {
        GD.Print("[UYARI] " + text);
        if (from.GetTree().GetFirstNodeInGroup("player") is PlayerController player)
        {
            player._noticeLabel.Text = text;
            player._noticeTime = 8.0;
        }
    }

    /// <summary>Puts an object straight into the player's hands.</summary>
    public void Give(RigidBody3D body)
    {
        _heldPart = body;
        body.Freeze = true;
    }

    /// <summary>What the player is carrying, or null.</summary>
    public RigidBody3D Held => _heldPart;
    public HandTool HeldTool => _heldPart as HandTool;

    /// <summary>Takes the carried object out of the player's hands without
    /// dropping it (it has just been fitted somewhere).</summary>
    public RigidBody3D ReleaseHeld()
    {
        var held = _heldPart;
        _heldPart = null;
        return held;
    }

    public override void _Ready()
    {
        var collision = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight / 2f, 0f), // body origin = feet, shape centered above it
        };
        AddChild(collision);

        BuildBodyVisual();

        _head = new Node3D { Position = new Vector3(0f, EyeHeight, 0f), Name = "Head" };
        AddChild(_head);

        _fpCamera = new Camera3D { Current = true, Name = "FirstPersonCamera" };
        _head.AddChild(_fpCamera);

        // Where a picked-up part is held -- child of _head so it moves and
        // tilts naturally with where the player looks, same as the cameras.
        _carryPoint = new Node3D { Position = new Vector3(0f, -0.1f, -0.6f), Name = "CarryPoint" };
        _head.AddChild(_carryPoint);

        // Sibling of _head (both direct children of the body), NOT nested
        // under it: parenting the arm under the already-pitched _head and
        // then adding a 180 deg child rotation on top composed badly and
        // inverted the effective pitch axis for the TP camera only (found
        // live -- FP pitch felt right, TP pitch felt backwards). Pitch is
        // applied to both independently, as flat Euler assignments, in
        // _Input below.
        _tpArm = new SpringArm3D { SpringLength = 3.0f, Name = "ThirdPersonArm", Position = new Vector3(0f, EyeHeight, 0f) };
        AddChild(_tpArm);
        _tpCamera = new Camera3D { Current = false, Name = "ThirdPersonCamera" };
        _tpArm.AddChild(_tpCamera);

        var ui = new CanvasLayer();
        AddChild(ui);

        _interactPrompt = new Label { Text = "[E] 2D panel   [Click] press a button", Visible = false };
        ui.AddChild(_interactPrompt);
        _interactPrompt.AnchorLeft = _interactPrompt.AnchorRight = 0.5f;
        _interactPrompt.AnchorTop = _interactPrompt.AnchorBottom = 0.85f;

        AddToGroup("player");
        _noticeLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _noticeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.25f));
        _noticeLabel.AddThemeFontSizeOverride("font_size", 22);
        ui.AddChild(_noticeLabel);
        _noticeLabel.AnchorLeft = 0f;
        _noticeLabel.AnchorRight = 1f;
        _noticeLabel.AnchorTop = _noticeLabel.AnchorBottom = 0.3f;

        _statusLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.6f));
        ui.AddChild(_statusLabel);
        _statusLabel.AnchorLeft = 0f;
        _statusLabel.AnchorRight = 1f;
        _statusLabel.AnchorTop = _statusLabel.AnchorBottom = 0.6f;

        _sheetLabel = new Label();
        _sheetLabel.AddThemeFontSizeOverride("font_size", 13);
        ui.AddChild(_sheetLabel);
        _sheetLabel.AnchorLeft = _sheetLabel.AnchorRight = 1f;
        _sheetLabel.AnchorTop = _sheetLabel.AnchorBottom = 0.32f;
        _sheetLabel.OffsetLeft = -330f;

        _heldLabel = new Label();
        ui.AddChild(_heldLabel);
        _heldLabel.AnchorLeft = _heldLabel.AnchorRight = 0.5f;
        _heldLabel.AnchorTop = _heldLabel.AnchorBottom = 0.92f;

        var crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center };
        ui.AddChild(crosshair);
        crosshair.AnchorLeft = crosshair.AnchorRight = crosshair.AnchorTop = crosshair.AnchorBottom = 0.5f;
        crosshair.OffsetLeft = crosshair.OffsetTop = -8;
        crosshair.OffsetRight = crosshair.OffsetBottom = 8;

        _operatorPanel = GetTree().Root.FindChild("OperatorPanel", true, false) as Control;

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // _Input (not _UnhandledInput): with the operator panel's Controls covering
    // a chunk of the screen, GUI mouse-motion handling could otherwise eat the
    // look input before it reached an Unhandled handler -- found live (mouse
    // look did nothing, WASD worked fine, which is exactly that symptom).
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-mm.Relative.X * MouseSensitivity);
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSensitivity, -1.3f, 1.3f);
            _head.Rotation = new Vector3(_pitch, 0f, 0f);
            // Flat assignment (not a child rotation nested under _head): pitch
            // and the "face backward to look at the character" 180 both land
            // in one Euler value, computed the same consistent way Godot
            // always resolves Rotation, instead of composing two separate
            // parent/child transforms that inverted pitch (see _Ready's note).
            _tpArm.Rotation = new Vector3(_pitch, Mathf.Pi, 0f);
        }
        else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            switch (keyEvent.Keycode)
            {
                case Key.V:
                    ToggleView();
                    break;
                case Key.Escape:
                    Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                        ? Input.MouseModeEnum.Visible
                        : Input.MouseModeEnum.Captured;
                    break;
                case Key.E when _atStation:
                    _station.SetPanelOpen(!_station.PanelOpen);
                    Input.MouseMode = _station.PanelOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
                    break;
                case Key.E when _atConsole:
                    ToggleConsoleUse();
                    break;
                case Key.F when Input.MouseMode == Input.MouseModeEnum.Captured:
                    TogglePickup();
                    break;
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel
                 && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            LookedAtInteractable()?.Turn(this, wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                if (LookedAtInteractable() is IInteractable target)
                    target.Use(this);
                else
                    TryPressConsoleButton(); // crosshair raycast -- see that method's doc comment
            }
            else if (!_atConsole && !_atStation)
            {
                // Safety net for a focus-timing edge case: clicking back into
                // the game window re-captures the mouse for looking around,
                // in case capture didn't take right at startup (before the
                // window had OS focus) and nothing else released it on
                // purpose.
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
        }
    }

    private Node3D LookedAt(float range)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return null;
        var from = camera.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(from, from - camera.GlobalTransform.Basis.Z * range);
        var exclude = new Godot.Collections.Array<Rid> { GetRid() };
        if (_heldPart != null)
            exclude.Add(_heldPart.GetRid());
        query.Exclude = exclude;
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
            return null;
        LookPoint = result["position"].AsVector3();
        return result["collider"].As<Node3D>();
    }

    private IInteractable LookedAtInteractable() => IInteractable.Find(LookedAt(PickupRange));

    /// <summary>Crosshair-style interaction (standard FPS pattern, since the
    /// mouse cursor is invisible/locked in Captured mode -- there's nothing
    /// visible to literally click ON, so "what you're looking at" stands in
    /// for the cursor): raycasts from the active camera straight ahead, and
    /// if it hits one of the console's real buttons within reach, presses it.</summary>
    private void TryPressConsoleButton()
    {
        if (_consoleStand == null || !_atConsole)
            return;
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;
        var from = camera.GlobalPosition;
        var to = from - camera.GlobalTransform.Basis.Z * InteractRange; // -Z = camera forward
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() }; // don't hit our own capsule
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count > 0 && result["collider"].As<Node3D>() is Node3D hit)
            _consoleStand.TryPress(hit);
    }

    /// <summary>Same crosshair-raycast pattern as TryPressConsoleButton, but
    /// not gated to console proximity and against anything in the
    /// "pickupable" group (see Main.ExtractFinishedPart) instead of the
    /// console's buttons.</summary>
    private void TogglePickup()
    {
        if (_heldPart != null)
        {
            _heldPart.Freeze = false;
            _heldPart = null;
            return;
        }

        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;
        var from = camera.GlobalPosition;
        var to = from - camera.GlobalTransform.Basis.Z * PickupRange;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count > 0 && result["collider"].As<Node3D>() is RigidBody3D body && body.IsInGroup("pickupable")
            && ((body as Carryable)?.CanPickUp ?? true))
        {
            (body as Carryable)?.OnPickedUp();
            _heldPart = body;
            _heldPart.Freeze = true; // kinematic while held -- carried by direct transform, not physics forces
            _heldPart.LinearVelocity = Vector3.Zero;
            _heldPart.AngularVelocity = Vector3.Zero;
        }
    }

    private void ToggleView()
    {
        _firstPerson = !_firstPerson;
        _fpCamera.Current = _firstPerson;
        _tpCamera.Current = !_firstPerson;
        _bodyVisual.Visible = !_firstPerson; // hide your own body in FP so it doesn't clip the camera
    }

    /// <summary>Simple primitive placeholder (capsule torso + sphere head) --
    /// no suitable CC0/CC-BY rigged humanoid was sourced (same situation as
    /// the machine body; see ASSETS.md). Only shown in third-person; a real
    /// FP view wouldn't want to see its own floating head anyway.</summary>
    private void BuildBodyVisual()
    {
        _bodyVisual = new Node3D { Name = "BodyVisual", Visible = false };
        AddChild(_bodyVisual);

        var skin = new StandardMaterial3D { AlbedoColor = new Color(0.65f, 0.55f, 0.45f), Roughness = 0.8f };

        _bodyVisual.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = CapsuleRadius, Height = CapsuleHeight - 0.2f },
            MaterialOverride = skin,
            Position = new Vector3(0f, CapsuleHeight / 2f - 0.1f, 0f),
        });
        _bodyVisual.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.12f },
            MaterialOverride = skin,
            Position = new Vector3(0f, CapsuleHeight - 0.12f, 0f),
        });
    }

    private void ToggleConsoleUse()
    {
        bool usingConsole = Input.MouseMode == Input.MouseModeEnum.Visible;
        Input.MouseMode = usingConsole ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (!usingConsole && _consoleStand.Rig != null)
            (_operatorPanel as Panel.OperatorPanel)?.SelectMachine(_consoleStand.Rig);
    }

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;
        if (!IsOnFloor())
            velocity.Y -= Gravity * (float)delta;
        else if (Input.IsKeyPressed(Key.Space))
            velocity.Y = JumpVelocity;

        Vector2 inputDir = new(
            (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0),
            (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0)
        );
        inputDir = inputDir.Normalized();
        float speed = WalkSpeed * (Input.IsKeyPressed(Key.Shift) ? RunSpeedMultiplier : 1f);
        var moveDir = (Transform.Basis * new Vector3(inputDir.X, 0f, inputDir.Y)).Normalized();
        velocity.X = moveDir.X * speed;
        velocity.Z = moveDir.Z * speed;

        Velocity = velocity;
        MoveAndSlide();

        UpdateConsoleProximity();
        UpdateStationProximity();
        UpdateCarriedPart();
        UpdatePickupPrompt();
    }

    private void UpdateConsoleProximity()
    {
        _consoleStand = null;
        foreach (var node in GetTree().GetNodesInGroup("console_stands"))
            if (node is ConsoleStand stand && (_consoleStand == null ||
                GlobalPosition.DistanceTo(stand.GlobalPosition) < GlobalPosition.DistanceTo(_consoleStand.GlobalPosition)))
                _consoleStand = stand;
        if (_consoleStand == null)
            return;
        bool inRange =GlobalPosition.DistanceTo(_consoleStand.GlobalPosition) <= InteractRange;
        if (inRange != _atConsole)
        {
            _atConsole = inRange;
            if (!inRange && _operatorPanel != null)
                Input.MouseMode = Input.MouseModeEnum.Captured; // walked away: give look control back
        }
    }

    private void UpdateStationProximity()
    {
        _station = null;
        foreach (var node in GetTree().GetNodesInGroup("stock_stations"))
            if (node is StockStation station && (_station == null ||
                GlobalPosition.DistanceTo(station.GlobalPosition) < GlobalPosition.DistanceTo(_station.GlobalPosition)))
                _station = station;
        bool inRange = _station != null && GlobalPosition.DistanceTo(_station.GlobalPosition) <= InteractRange + 0.4f;
        if (_atStation && !inRange && _station != null && _station.PanelOpen)
        {
            _station.SetPanelOpen(false);
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        _atStation = inRange;
    }

    private void UpdateCarriedPart()
    {
        if (_heldPart != null)
            _heldPart.GlobalTransform = _carryPoint.GlobalTransform;
    }

    /// <summary>Console prompt takes priority (it's the proven, primary
    /// interaction); otherwise show pickup/drop based on a crosshair raycast
    /// against "pickupable" bodies, same cost class as the console-proximity
    /// check above (one ray per physics frame).</summary>
    private void UpdatePickupPrompt()
    {
        _statusLabel.Text = ToolStatus ?? "";
        ToolStatus = null;
        ISetupSheet nearest = null;
        foreach (var node in GetTree().GetNodesInGroup("setup_sheets"))
            if (node is ISetupSheet sheet && GlobalPosition.DistanceTo(sheet.SheetPosition) < 4f
                && (nearest == null || GlobalPosition.DistanceTo(sheet.SheetPosition) < GlobalPosition.DistanceTo(nearest.SheetPosition)))
                nearest = sheet;
        _sheetLabel.Text = nearest?.SheetText ?? "";
        _noticeTime -= GetPhysicsProcessDeltaTime();
        if (_noticeTime <= 0.0)
            _noticeLabel.Text = "";
        _heldLabel.Text = _heldPart == null ? "" : "Elde: " + ((_heldPart as Carryable)?.DisplayName ?? "parça");

        if (_atStation)
        {
            _interactPrompt.Text = "[E] Kütük istasyonu";
            _interactPrompt.Visible = true;
            return;
        }

        if (_atConsole)
        {
            _interactPrompt.Text = "[E] 2D panel   [Click] press a button";
            _interactPrompt.Visible = true;
            return;
        }

        if (LookedAtInteractable()?.Prompt(this) is string hint)
        {
            _interactPrompt.Text = hint;
            _interactPrompt.Visible = true;
            return;
        }

        if (_heldPart != null)
        {
            _interactPrompt.Text = "[F] Bırak";
            _interactPrompt.Visible = true;
            return;
        }

        var camera = GetViewport().GetCamera3D();
        if (camera != null)
        {
            var from = camera.GlobalPosition;
            var to = from - camera.GlobalTransform.Basis.Z * PickupRange;
            var query = PhysicsRayQueryParameters3D.Create(from, to);
            query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
            var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (result.Count > 0 && result["collider"].As<Node3D>() is RigidBody3D body && body.IsInGroup("pickupable")
                && ((body as Carryable)?.CanPickUp ?? true))
            {
                _interactPrompt.Text = "[F] Al: " + ((body as Carryable)?.DisplayName ?? "parça");
                _interactPrompt.Visible = true;
                return;
            }
        }

        _interactPrompt.Visible = false;
    }
}
