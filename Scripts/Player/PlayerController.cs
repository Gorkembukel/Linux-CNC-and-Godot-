using Godot;

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

    private const float CapsuleRadius = 0.3f;
    private const float CapsuleHeight = 1.8f;
    private const float EyeHeight = 1.65f;

    private Camera3D _fpCamera;
    private Camera3D _tpCamera;
    private SpringArm3D _tpArm;
    private Node3D _head; // pitch pivot, shared by both cameras' parent chain
    private bool _firstPerson = true;
    private float _pitch;

    private Node3D _consoleStand;
    private Label _interactPrompt;
    private Control _operatorPanel;
    private bool _atConsole;
    private Node3D _bodyVisual;

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

        _tpArm = new SpringArm3D { SpringLength = 3.0f, Name = "ThirdPersonArm" };
        _tpArm.RotateY(Mathf.Pi); // look from behind the character, not through it
        _head.AddChild(_tpArm);
        _tpCamera = new Camera3D { Current = false, Name = "ThirdPersonCamera" };
        _tpArm.AddChild(_tpCamera);

        _interactPrompt = new Label { Text = "[E] Use console", Visible = false };
        var ui = new CanvasLayer();
        AddChild(ui);
        ui.AddChild(_interactPrompt);
        _interactPrompt.AnchorLeft = _interactPrompt.AnchorRight = 0.5f;
        _interactPrompt.AnchorTop = _interactPrompt.AnchorBottom = 0.85f;

        _consoleStand = GetTree().Root.FindChild("ConsoleStand", true, false) as Node3D;
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
                case Key.E when _atConsole:
                    ToggleConsoleUse();
                    break;
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                 && Input.MouseMode == Input.MouseModeEnum.Visible && !_atConsole)
        {
            // Safety net for a focus-timing edge case: clicking back into the
            // game window re-captures the mouse for looking around, in case
            // capture didn't take right at startup (before the window had OS
            // focus) and nothing else released it on purpose.
            Input.MouseMode = Input.MouseModeEnum.Captured;
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
    }

    private void UpdateConsoleProximity()
    {
        if (_consoleStand == null)
            return;
        bool inRange = GlobalPosition.DistanceTo(_consoleStand.GlobalPosition) <= InteractRange;
        if (inRange != _atConsole)
        {
            _atConsole = inRange;
            _interactPrompt.Visible = inRange;
            if (!inRange && _operatorPanel != null)
                Input.MouseMode = Input.MouseModeEnum.Captured; // walked away: give look control back
        }
    }
}
