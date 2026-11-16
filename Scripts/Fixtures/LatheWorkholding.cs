using System.Collections.Generic;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Interaction;
using CncTrainer.Material;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer.Fixtures;

/// <summary>
/// Hands-on setup of a lathe: the bar the player puts in the chuck becomes
/// the stock being turned, as far out of the jaws as they left it. A chuck
/// that is not tight throws the bar on the first real cut, a key left in the
/// chuck flies off when the spindle starts, and every tool's tip sits a
/// little off from where the control believes it is until the player
/// measures a cut and touches the tool off. Child of the machine root.
/// </summary>
public partial class LatheWorkholding : Node3D, ISetupSheet
{
    private const float M = WorldScale.MmToM;
    private const float MaxSpinDegPerSec = 600f;

    public JawChuck Chuck { get; private set; }

    private MachineSpec _spec;
    private ProceduralLatheBody _body;
    private LatheCutting _cutting;
    private WorkpieceVisual _workpiece;
    private BridgeClient _bridge;
    private readonly Dictionary<int, Vector2> _tipErrors = new();
    private float _zOffsetAtPlacement;
    private bool _crashed;

    public Vector3 SheetPosition => _body.GlobalPosition;

    public void Setup(MachineSpec spec, ProceduralLatheBody body, LatheCutting cutting, WorkpieceVisual workpiece, BridgeClient bridge)
    {
        _spec = spec;
        _body = body;
        _cutting = cutting;
        _workpiece = workpiece;
        _bridge = bridge;
        Chuck = body.Chuck;

        var random = new RandomNumberGenerator();
        random.Randomize();
        foreach (var tool in spec.Tools)
            if (tool != null)
                _tipErrors[tool.Number] = new Vector2(
                    random.RandfRange(0.08f, 0.3f) * (random.Randf() < 0.5f ? -1f : 1f),
                    random.RandfRange(0.1f, 0.4f) * (random.Randf() < 0.5f ? -1f : 1f));

        cutting.TipError = TipErrorOf;
        cutting.IsSecure = () => Chuck.Grip.HoldsFirmly;
        cutting.CutWhileLoose = ThrowBar;
        cutting.Severed = () => Chuck.SetBarLength(LiveLengthMm());
        Chuck.Changed = OnBarChanged;
        Chuck.TakeMachined = () => _cutting.HasCut ? MakePart() : null;
        Chuck.Measure = Measure;
        AddToGroup("setup_sheets");
    }

    public Vector2 TipErrorOf(int toolNumber) => _tipErrors.TryGetValue(toolNumber, out var error) ? error : Vector2.Zero;

    /// <summary>For tests: fixes a tool's hidden tip error (radius, Z).</summary>
    public void SetTipError(int toolNumber, Vector2 error) => _tipErrors[toolNumber] = error;

    private float LiveLengthMm() => (_workpiece.Profile.LiveRadii().Length - 1) * _workpiece.Profile.StepMm;

    /// <summary>Rebuilds the stock being cut from the bar in the chuck,
    /// keeping whatever shape has already been turned into it.</summary>
    private void OnBarChanged()
    {
        var grip = Chuck.Grip;
        if (!grip.HasBar)
        {
            _workpiece.SetProfile(null);
            return;
        }

        float[] radii;
        float step = 0.5f;
        if (_workpiece.Profile == null)
        {
            radii = new float[Mathf.Max(2, Mathf.RoundToInt(grip.BarLengthMm / step) + 1)];
            System.Array.Fill(radii, grip.BarDiameterMm / 2f);
            _cutting.Material = Chuck.Stock.Blank.Material;
            _cutting.ResetStock();
            _zOffsetAtPlacement = _bridge.State.WorkOffset.Z;
        }
        else
            radii = _workpiece.Profile.LiveRadii();

        _workpiece.SetProfile(new LatheRadiusProfile(radii, _body.JawFrontZMm + grip.StickOutMm - (radii.Length - 1) * step, step));
        _workpiece.StockVisualMaterial.AlbedoColor = MaterialPreset.Get(_cutting.Material).BaseColor;
        Chuck.ShowRawStock(false);
    }

    /// <summary>What is left in the chuck as a loose object.</summary>
    private Carryable MakePart()
    {
        var profile = _workpiece.Profile;
        var radii = profile.LiveRadii();
        var part = new Carryable { Name = "TurnedPart", DisplayName = "Tornalanmış parça" };
        float centreZ = profile.ZMin;
        if (WorkpiecePartBuilder.Build(radii, profile.StepMm, profile.ZMin, MaterialPreset.Get(_cutting.Material).DensityKgPerM3)
            is WorkpiecePartBuilder.BuiltPart built)
        {
            part.Mass = Mathf.Max(0.01f, built.MassKg);
            part.AddChild(new MeshInstance3D { Mesh = built.Mesh, MaterialOverride = _workpiece.StockVisualMaterial.Duplicate() as Godot.Material });
            part.AddChild(new CollisionShape3D { Shape = built.Collision });
            centreZ = built.CentroidZmm;
        }
        else
        {
            // Too short a stub to revolve: a plain disc.
            float radius = Mathf.Max(0.003f, (radii.Length > 0 ? radii[0] : 5f) * M);
            part.Mass = 0.02f;
            BodyParts.Cylinder(part, "Stub", radius, 0.003f, Vector3.Zero, Vector3.Right, BodyParts.Steel);
            part.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.003f, radius * 2f, radius * 2f) } });
        }
        GetTree().CurrentScene.AddChild(part);
        part.GlobalTransform = _body.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(centreZ * M, 0f, 0f));
        return part;
    }

    private void ThrowBar()
    {
        bool gripTooShort = Chuck.Grip.Torque >= 2;
        var part = Chuck.TakeBar();
        part.Freeze = false;
        part.ApplyCentralImpulse(GlobalTransform.Basis * new Vector3(0.8f, 1.2f, 1.5f) * part.Mass * 2.5f);
        StopMachine(gripTooShort
            ? "PARÇA FIRLADI: kütük çenelerin içinde çok az tutuluyordu (en az 15 mm)"
            : "PARÇA FIRLADI: ayna sıkılmadan kesime girildi");
    }

    private string Measure(Vector3 globalPoint)
    {
        if (System.Math.Abs(_bridge.State.SpindleRpm) > 1.0)
            return "Kumpas: iş mili dönerken ölçülmez";
        var profile = _workpiece.Profile;
        float z = _body.ToLocal(globalPoint).X / M;
        float diameter = Mathf.Round(DiameterAt(z) / 0.02f) * 0.02f;
        float fromTip = profile.ZMin + LiveLengthMm() - z;
        return System.FormattableString.Invariant($"Kumpas: Ø{diameter:0.00} mm   (uçtan {Mathf.Max(0f, fromTip):0} mm içeride)");
    }

    /// <summary>True diameter of the bar at a machine Z (mm).</summary>
    public float DiameterAt(float machineZ) =>
        _workpiece.Profile == null ? 0f : 2f * _workpiece.Profile.RadiusAt(_workpiece.Profile.IndexAt(machineZ));

    public override void _Process(double delta)
    {
        var s = _bridge.State;
        float rpm = (float)System.Math.Abs(s.SpindleRpm);
        Chuck.Spin(Mathf.Min(rpm * 6f, MaxSpinDegPerSec) * (float)delta);

        if (Chuck.Grip.KeyIn && rpm > 1f)
        {
            var key = Chuck.TakeKey();
            key.ApplyCentralImpulse(GlobalTransform.Basis * new Vector3(0.3f, 1.5f, 2f) * key.Mass * 3f);
            StopMachine("AYNA ANAHTARI FIRLADI: anahtar aynada unutuldu, iş mili çalıştırıldı");
        }

        var tip = _cutting.TipAt(s.Position, s.ToolNumber);
        bool inChuck = tip.Y < _body.JawFrontZMm + 0.5f && tip.X < Chuck.RadiusM / M + 2f;
        if (inChuck && !_crashed && s.MachineOn)
            StopMachine("ÇARPMA: takım aynaya girdi");
        _crashed = inChuck;
    }

    public string SheetText
    {
        get
        {
            var grip = Chuck.Grip;
            var state = _bridge.State;
            static string Mark(bool done) => done ? "[x] " : "[ ] ";
            string text = $"AYAR FÖYÜ - {_spec.MachineName}\n";
            text += Chuck.SheetLines;
            if (!grip.HasBar)
                return text;
            if (grip.StickOutMm > 3f * grip.BarDiameterMm)
                text += "    Çıkıntı çapın 3 katından fazla: titreşim\n";
            if (_body.JawFrontZMm + grip.StickOutMm > (_spec.FindAxis(AxisId.Z)?.MaxLimit ?? 0f) - 5f)
                text += "    Kütük ucu Z kursunun dışında: içeri it\n";
            text += Mark(Mathf.Abs(state.WorkOffset.Z - _zOffsetAtPlacement) > 1e-4f) + "Z sıfırı (alın tornala, TOUCH OFF Z 0)\n";
            text += Mark(Mathf.Abs(state.WorkOffset.X) > 1e-4f) + $"T{state.ToolNumber} X ofseti (paso al, kumpasla ölç,\n    TOUCH OFF X ölçülen çap)\n";
            return text;
        }
    }

    private static string Inv(System.FormattableString text) => System.FormattableString.Invariant(text);

    private void StopMachine(string reason)
    {
        _bridge.StopProgram();
        _bridge.SpindleOff();
        _bridge.MachineOff();
        PlayerController.Notify(this, reason);
    }
}
