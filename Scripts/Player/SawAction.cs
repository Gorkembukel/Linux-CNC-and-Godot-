using Godot;
using CncTrainer.Chips;
using CncTrainer.Interaction;

namespace CncTrainer.Player;

/// <summary>
/// Sawing by hand with the hacksaw in hand: the crosshair on something that
/// can be sawn shows where the cut would go; a click marks it, and from then
/// on the mouse pushes the saw forward (mouse forward) and draws it back.
/// Forward strokes cut, slow full strokes best; T finishes the cut at once,
/// right click or Esc gives it up. While sawing the view and the feet stay put.
/// </summary>
public partial class SawAction : Node3D
{
    public PlayerController Player;

    /// <summary>Half the stroke along the blade (m).</summary>
    private const float StrokeHalfM = 0.07f;
    /// <summary>Saw travel per pixel of mouse movement (m).</summary>
    private const float MetresPerPixel = 0.0006f;
    /// <summary>Faster than this the teeth skid and heat up instead of cutting (m/s).</summary>
    private const float TooFastMPerS = 0.45f;

    private ISawable _target;
    private SawCut? _planned;
    private SawCut _cut;
    private bool _locked;
    private float _removedMm2, _stroke, _speed, _angleDeg;
    private const float AngleStepDeg = 5f, MaxAngleDeg = 60f;
    private double _warned;
    private MeshInstance3D _preview, _kerf;

    private RigidBody3D _heldStill;

    public bool Locked => _locked;

    /// <summary>Where the saw sits while cutting; null when it is just carried.</summary>
    public Transform3D? HeldTransform { get; private set; }

    /// <summary>What to show under the crosshair, or null.</summary>
    public string Status { get; private set; }

    private bool HoldingSaw => Player.HeldTool?.Kind == HandToolKind.Hacksaw;

    public override void _Ready()
    {
        _preview = Plane(new Color(1f, 0.25f, 0.2f, 0.45f));
        _kerf = Plane(new Color(0.05f, 0.05f, 0.05f, 1f));
    }

    private MeshInstance3D Plane(Color color)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = color.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            },
            TopLevel = true,
            Visible = false,
        };
        AddChild(mesh);
        return mesh;
    }

    /// <summary>A thin slab in the cut plane: `depth` down from the top of
    /// the section, `width` across, `thick` along the normal.</summary>
    private static Transform3D Slab(SawCut cut, float depth, float width, float thick)
    {
        var basis = new Basis(cut.Normal * thick, -cut.Down * depth, cut.Across * width);
        return new Transform3D(basis, cut.Center - cut.Down * (cut.DepthM / 2f) + cut.Down * (depth / 2f));
    }

    /// <summary>Every physics frame, with what the crosshair is on.</summary>
    public void Tick(Node3D lookedAt, Vector3 lookPoint, double delta)
    {
        Status = null;
        _warned -= delta;
        if (!HoldingSaw)
        {
            Cancel();
            _preview.Visible = false;
            return;
        }
        if (_locked)
        {
            float progress = Mathf.Clamp(_removedMm2 / Mathf.Max(_cut.AreaMm2, 1f), 0f, 1f);
            _kerf.Visible = progress > 0.002f;
            if (_kerf.Visible)
                _kerf.GlobalTransform = Slab(_cut, _cut.DepthM * progress, _cut.WidthM * 1.02f, BenchKerfM);
            var sawBasis = new Basis(_cut.Across, -_cut.Down, _cut.Across.Cross(-_cut.Down));
            var edge = _cut.Center - _cut.Down * (_cut.DepthM / 2f) + _cut.Down * (_cut.DepthM * progress);
            HeldTransform = new Transform3D(sawBasis, edge + _cut.Across * _stroke - _cut.Down * 0.006f);
            Status = System.FormattableString.Invariant(
                $"Kesim %{progress * 100f:0}   [Fare ileri-geri] testere   [T] kesimi bitir   [Sağ tık] vazgeç");
            if (progress >= 1f)
                Complete();
            return;
        }

        _target = ISawable.Find(lookedAt);
        string why = null;
        _planned = _target?.PlanCut(lookPoint, _angleDeg, out why);
        _preview.Visible = _planned != null;
        if (_planned is { } plan)
        {
            _preview.GlobalTransform = Slab(plan, plan.DepthM * 1.1f, plan.WidthM * 1.1f, 0.0015f);
            Status = System.FormattableString.Invariant($"[Sol tık] kesim yerini işaretle   [Teker] açı: {_angleDeg:0}°   (kesit {plan.AreaMm2:0} mm²)");
        }
        else if (why != null)
            Status = System.FormattableString.Invariant($"{why}   [Teker] açı: {_angleDeg:0}°");
    }

    private const float BenchKerfM = 0.001f;

    /// <summary>True when the saw took the event.</summary>
    public bool HandleInput(InputEvent @event)
    {
        if (!HoldingSaw)
            return false;
        if (!_locked)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel && _target != null)
            {
                _angleDeg = Mathf.Clamp(_angleDeg + (wheel.ButtonIndex == MouseButton.WheelUp ? AngleStepDeg : -AngleStepDeg), -MaxAngleDeg, MaxAngleDeg);
                return true;
            }
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _planned is { } plan)
            {
                _cut = plan;
                _locked = true;
                // A loose piece is held still while it is sawn.
                if (_target is RigidBody3D { Freeze: false } loose)
                {
                    loose.Freeze = true;
                    _heldStill = loose;
                }
                _removedMm2 = 0f;
                _stroke = 0f;
                _preview.Visible = false;
                return true;
            }
            return false;
        }

        switch (@event)
        {
            case InputEventMouseMotion motion:
                Stroke(-motion.Relative.Y * MetresPerPixel, motion);
                return true;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.T }:
                _removedMm2 = _cut.AreaMm2;
                return true;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                Cancel();
                return true;
            case InputEventMouseButton:
                return true;
        }
        return false;
    }

    private void Stroke(float along, InputEventMouseMotion motion)
    {
        float before = _stroke;
        _stroke = Mathf.Clamp(_stroke + along, -StrokeHalfM, StrokeHalfM);
        float moved = _stroke - before;
        _speed = Mathf.Abs(motion.Velocity.Y) * MetresPerPixel;
        if (moved <= 0f)
            return;
        float rate = RateMm2PerM(_target.SawMaterial);
        if (_speed > TooFastMPerS)
        {
            rate *= 0.3f;
            if (_warned <= 0)
            {
                PlayerController.Notify(this, "Testere çok hızlı: dişler kayar, ağız ısınıp körelir. Yavaş ve tam boy çek.");
                _warned = 4.0;
            }
        }
        _removedMm2 += moved * rate;
    }

    /// <summary>Section a forward stroke gets through, per metre of stroke.</summary>
    private static float RateMm2PerM(MaterialKind material) => MaterialPreset.Get(material).SawRateMm2PerM;

    private void Complete()
    {
        var target = _target;
        var cut = _cut;
        Cancel();
        target.FinishCut(cut);
    }

    private void Cancel()
    {
        _locked = false;
        if (IsInstanceValid(_heldStill))
            _heldStill.Freeze = false;
        _heldStill = null;
        _kerf.Visible = false;
        HeldTransform = null;
    }
}
