using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Interaction;
using CncTrainer.Machining;
using CncTrainer.Material;
using CncTrainer.Procedural;
using CncTrainer.Spec;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>
/// Hands-on setup of a lathe: the bar the player puts in the chuck becomes
/// the stock being turned, as far out of the jaws as they left it. A chuck
/// that is not tight throws the bar on the first real cut, a key left in the
/// chuck flies off when the spindle starts. The turret starts empty: the
/// player loads it, and every tool's tip sits a little off from where the
/// control believes it is until a cut is measured and the tool touched off.
/// </summary>
public partial class LatheWorkholding : MachineSetup
{
    private const float M = WorldScale.MmToM;
    private const float MaxSpinDegPerSec = 600f;
    private const float ProfileStepMm = 0.1f;

    public JawChuck Chuck { get; private set; }
    public ToolSocket Turret { get; private set; }

    private ProceduralLatheBody _body;
    private LatheCutting _cutting;
    private WorkpieceVisual _workpiece;
    private float _zOffsetAtPlacement;

    public override Vector3 SheetPosition => _body.GlobalPosition;

    public void Setup(MachineSpec spec, ProceduralLatheBody body, LatheCutting cutting, WorkpieceVisual workpiece,
        BridgeClient bridge, MachineSafety safety)
    {
        Init(spec, bridge, safety);
        _body = body;
        _cutting = cutting;
        _workpiece = workpiece;
        Chuck = body.Chuck;

        Turret = ToolSocket.Create(body.StationCount, turret: true, new Vector3(0.16f, 0.2f, 0.2f), new Vector3(0.06f, body.ToolReachM, 0f));
        Turret.PocketOf = body.StationOf;
        Turret.ActiveNumber = () => Bridge.State.ToolNumber;
        Turret.Turning = () => System.Math.Abs(Bridge.State.SpindleRpm) > 1.0;
        Turret.Changed += pocket => body.SetStationTool(pocket, Turret.Pocket(pocket)?.Spec);
        body.CrossSlide.AddChild(Turret);
        cutting.Tools = Turret;
        AddDoor(body, body.DoorOpening);
        cutting.IsSecure = () => Chuck.HoldsFirmly;
        cutting.CutWhileLoose = () =>
        {
            string reason = Chuck.LooseReason;
            Throw(Chuck.TakeBar(), reason);
        };
        cutting.Severed = () => Chuck.SetBarLength(LiveLengthMm());
        Chuck.Changed = OnBarChanged;
        Chuck.TakeMachined = () => _cutting.HasCut || Chuck.Stock?.Turned != null ? MakePart() : null;
        Chuck.Measure = Measure;
    }

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

        float[] radii, inner = null;
        var square = _workpiece.Profile?.SquareHalfMm ?? Vector2.Zero;
        if (_workpiece.Profile == null)
        {
            if (Chuck.Stock.Turned is { } shaped)
            {
                radii = (float[])shaped.RadiiMm.Clone();
                inner = (float[])shaped.InnerMm.Clone();
                square = shaped.SquareHalfMm;
            }
            else
            {
                radii = new float[Mathf.Max(2, Mathf.RoundToInt(grip.BarLengthMm / ProfileStepMm) + 1)];
                float radius = grip.BarDiameterMm / 2f;
                if (Chuck.Stock.Blank.Shape == StockShape.Block)
                {
                    var across = Chuck.Seat.ExtentsM / M;
                    square = new Vector2(across.Y, across.Z) / 2f;
                    radius = LatheSection.Corner(square);
                }
                System.Array.Fill(radii, radius);
            }
            _cutting.Material = Chuck.Stock.Blank.Material;
            _cutting.ResetStock();
            _zOffsetAtPlacement = Bridge.State.WorkOffset.Z;
        }
        else
        {
            radii = _workpiece.Profile.LiveRadii();
            inner = _workpiece.Profile.LiveInner();
        }

        _workpiece.SetProfile(new LatheRadiusProfile(radii,
            _body.JawFrontZMm + grip.StickOutMm - (radii.Length - 1) * ProfileStepMm, ProfileStepMm, inner, square));
        _workpiece.StockVisualMaterial.AlbedoColor = MaterialPreset.Get(_cutting.Material).BaseColor;
        Chuck.ShowRawStock(false);
    }

    /// <summary>What is left in the chuck as a loose object.</summary>
    private Carryable MakePart()
    {
        var profile = _workpiece.Profile;
        var radii = profile.LiveRadii();
        Carryable part = StockItem.CreateTurned(radii, profile.LiveInner(), profile.StepMm, _cutting.Material, profile.SquareHalfMm);
        float centreZ = profile.ZMin + (radii.Length - 1) * profile.StepMm / 2f;
        if (part == null)
        {
            // Too short a stub to revolve: a plain disc.
            float radius = Mathf.Max(0.003f, (radii.Length > 0 ? radii[0] : 5f) * M);
            part = new Carryable { Name = "Stub", DisplayName = "Artık parça", Mass = 0.02f };
            BodyParts.Cylinder(part, "Stub", radius, 0.003f, Vector3.Zero, Vector3.Right, BodyParts.Steel);
            part.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.003f, radius * 2f, radius * 2f) } });
        }
        else
            part.DisplayName += _cutting.SurfaceNote();
        _cutting.PartFinished?.Invoke();
        GetTree().CurrentScene.AddChild(part);
        part.GlobalTransform = _body.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(centreZ * M, 0f, 0f));
        return part;
    }

    private string Measure(HandToolKind gauge, Vector3 globalPoint)
    {
        if (Gauges.Diameter(gauge, 0f) == null)
            return null;
        if (System.Math.Abs(Bridge.State.SpindleRpm) > 1.0)
            return "İş mili dönerken ölçülmez";
        var profile = _workpiece.Profile;
        var local = _workpiece.ToLocal(globalPoint) / M;
        float z = _body.ToLocal(globalPoint).X / M;
        float fromTip = profile.ZMin + LiveLengthMm() - z;
        int index = profile.IndexAt(z);
        return StockItem.SectionReading(gauge, new Vector2(local.Y, local.Z).Length(), profile.RadiusAt(index), profile.InnerAt(index), profile.SquareHalfMm)
            + System.FormattableString.Invariant($"   (uçtan {Mathf.Max(0f, fromTip):0} mm içeride)");
    }

    /// <summary>True diameter of the bar at a machine Z (mm).</summary>
    public float DiameterAt(float machineZ) =>
        _workpiece.Profile == null ? 0f : 2f * _workpiece.Profile.RadiusAt(_workpiece.Profile.IndexAt(machineZ));

    public override void _Process(double delta)
    {
        var s = Bridge.State;
        float rpm = (float)System.Math.Abs(s.SpindleRpm);
        float spin = Mathf.Min(rpm * 6f, MaxSpinDegPerSec) * (float)delta;
        Chuck.Spin(spin);
        _workpiece.RotateX(Mathf.DegToRad(spin));
        CheckDoorInterlock();
        CheckKeyLeftIn(Chuck, rpm > 1f, "AYNA ANAHTARI FIRLADI: anahtar aynada unutuldu, iş mili çalıştırıldı");
    }

    protected override string SheetBody
    {
        get
        {
            var grip = Chuck.Grip;
            var state = Bridge.State;
            var active = Turret.Active(state.ToolNumber);
            string text = Sheet.Mark(Turret.LoadedCount > 0) + $"Tarette takım  {Turret.LoadedCount}/{Turret.PocketCount}"
                + (state.ToolNumber > 0 && !active.Present ? $"  (T{state.ToolNumber} istasyonu BOŞ)" : "") + "\n";
            text += Chuck.SheetLines;
            if (!grip.HasBar)
                return text;
            if (grip.StickOutMm > 3f * grip.BarDiameterMm)
                text += "    Çıkıntı çapın 3 katından fazla: titreşim\n";
            if (_body.JawFrontZMm + grip.StickOutMm > (Spec.FindAxis(AxisId.Z)?.MaxLimit ?? 0f) - 5f)
                text += "    Kütük ucu Z kursunun dışında: içeri it\n";
            text += Sheet.Mark(Mathf.Abs(state.WorkOffset.Z - _zOffsetAtPlacement) > 1e-4f) + "Z sıfırı (alın tornala, TOUCH OFF Z 0)\n";
            text += Sheet.Mark(Mathf.Abs(state.WorkOffset.X) > 1e-4f) + $"T{state.ToolNumber} X ofseti (paso al, kumpasla ölç,\n    TOUCH OFF X ölçülen çap)\n";
            return text;
        }
    }
}
