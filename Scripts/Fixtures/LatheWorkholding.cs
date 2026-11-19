using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Interaction;
using CncTrainer.Machining;
using CncTrainer.Material;
using CncTrainer.Player;
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
public partial class LatheWorkholding : MachineSetup, ISawable, IThreaded
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
        AddJawRack(spec.Lathe);
        cutting.IsSecure = () => Chuck.HoldsFirmly;
        cutting.WorkSlenderness = () => Chuck.Grip.Slenderness;
        cutting.CutWhileLoose = () =>
        {
            string reason = Chuck.LooseReason;
            Throw(Chuck.TakeBar(), reason);
        };
        cutting.Severed = () => Chuck.SetBarLength(LiveLengthMm());
        Chuck.Changed = OnBarChanged;
        Chuck.TakeMachined = () => _cutting.HasCut || Chuck.Stock?.Turned != null ? MakePart() : null;
        Chuck.Measure = Measure;
        Chuck.Saw = this;
        Chuck.Threads = this;
        Chuck.UseWith = UseOnChuck;
        Chuck.TurnWith = TurnByHand;
        Chuck.PromptWith = IndicatorPrompt;
        Chuck.WhyNotMount = WhyNotMount;
        Chuck.SpindleTurning = () => !SpindleStopped;
        Chuck.Nose = spec.Lathe.SpindleNose;
        Chuck.Refitted = () =>
        {
            if (Chuck.Mounted)
                _body.FitChuck(Chuck.Model);
            else
                _body.FitBareNose(SpindleNoses.DiameterMm(spec.Lathe.SpindleNose));
            _cutting.ResetStock();
        };
    }

    /// <summary>A stand left of the control: on the shelf the jaws that are
    /// not in the chuck (reversed hard jaws for big parts, soft jaws to bore
    /// for a part that must run true), each numbered, the collets and the
    /// cam key; on the floor beside it the spare chucks, each with its own
    /// jaws in.</summary>
    private void AddJawRack(LatheStructure lathe)
    {
        var rack = new Node3D { Name = "JawRack", Position = new Vector3((lathe.StockZMin - lathe.HeadstockLengthMm - 900f) * M, 0f, (lathe.BedDepthMm / 2f + 300f) * M) };
        AddChild(rack);
        BodyParts.SolidBox(rack, "Shelf", new Vector3(0.5f, 0.04f, 0.35f), new Vector3(0f, 0.83f, 0f), BodyParts.DarkSteel);
        BodyParts.SolidBox(rack, "Post", new Vector3(0.05f, 0.81f, 0.05f), new Vector3(0f, 0.405f, 0f), BodyParts.DarkSteel);
        void Put(Carryable item, Vector3 at)
        {
            item.Freeze = true;
            rack.AddChild(item);
            item.Position = at;
        }
        int jaws = Chuck.Grip.Jaws.SlotCount;
        foreach (var (kind, z) in new[] { (JawKind.HardReversed, -0.1f), (JawKind.Soft, 0.0f) })
            for (int n = 1; n <= jaws; n++)
                Put(JawItem.Create(kind, n, Chuck.Model), new Vector3(-0.2f + n * 0.05f, 0.85f, z));
        Put(HandTool.Create(HandToolKind.CamKey), new Vector3(0.12f, 0.87f, -0.1f));

        var spares = lathe.Spares();
        for (int i = 0; i < spares.Length; i++)
        {
            var model = spares[i];
            var grip = new ChuckGrip(model.Dims(lathe.HeadstockLengthMm));
            Put(ChuckItem.Create(model, grip), new Vector3(-0.15f + i * 0.3f, 0.02f, 0.35f));
            for (int c = 0; c < model.ColletSizesMm.Length; c++)
                Put(ColletItem.Create(model.ColletSizesMm[c]), new Vector3(0.05f + c * 0.035f, 0.85f, 0.1f));
        }
    }

    /// <summary>Why the lathe will not take this chuck, or null.</summary>
    private string WhyNotMount(ChuckModel model) =>
        model.Mount != Spec.Lathe.SpindleNose
            ? $"{model.DisplayName} {SpindleNoses.NameOf(model.Mount)} burun için: bu tezgahın fener mili {SpindleNoses.NameOf(Spec.Lathe.SpindleNose)}"
        : model.DiameterMm > _body.MaxChuckDiameterMm
            ? System.FormattableString.Invariant($"{model.DisplayName} (Ø{model.DiameterMm:0}) bu tezgaha büyük: yatağa çarpar (en fazla Ø{_body.MaxChuckDiameterMm:0})")
            : null;

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
        var turned = StockItem.CreateTurned(radii, profile.LiveInner(), profile.StepMm, _cutting.Material, profile.SquareHalfMm);
        turned?.FinishSurface(_cutting.Surface());
        Carryable part = turned;
        float centreZ = profile.ZMin + (radii.Length - 1) * profile.StepMm / 2f;
        if (part == null)
        {
            // Too short a stub to revolve: a plain disc.
            float radius = Mathf.Max(0.003f, (radii.Length > 0 ? radii[0] : 5f) * M);
            part = new Carryable { Name = "Stub", DisplayName = "Artık parça", Mass = 0.02f };
            BodyParts.Cylinder(part, "Stub", radius, 0.003f, Vector3.Zero, Vector3.Right, BodyParts.Steel);
            part.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.003f, radius * 2f, radius * 2f) } });
        }
        _cutting.PartFinished?.Invoke();
        GetTree().CurrentScene.AddChild(part);
        part.GlobalTransform = _body.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(centreZ * M, 0f, 0f));
        return part;
    }

    private string Measure(HandToolKind gauge, Vector3 globalPoint)
    {
        if (gauge == HandToolKind.PitchGauge)
            return System.Math.Abs(Bridge.State.SpindleRpm) > 1.0 ? "İş mili dönerken ölçülmez" : Gauges.Pitch(ThreadAt(globalPoint, out _));
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

    public MaterialKind SawMaterial => _cutting.Material;

    /// <summary>A saw cut across the bar outside the jaws, with the spindle stopped.</summary>
    public SawCut? PlanCut(Vector3 globalPoint, float angleDeg, out string why)
    {
        why = null;
        var profile = _workpiece.Profile;
        if (profile == null)
            return null;
        if (System.Math.Abs(Bridge.State.SpindleRpm) > 1.0)
        {
            why = "İş mili dönerken testere vurulmaz!";
            return null;
        }
        if (angleDeg != 0f)
        {
            why = "Aynadaki parça yalnız dik kesilir (açıyı sıfırla)";
            return null;
        }
        float z = _workpiece.ToLocal(globalPoint).X / M;
        if (z < _body.JawFrontZMm + 3f)
        {
            why = "Çenelerin içi kesilmez";
            return null;
        }
        if (z > profile.ZMin + LiveLengthMm() - 2f)
        {
            why = "Uca çok yakın";
            return null;
        }
        int i = profile.IndexAt(z);
        float r = profile.RadiusAt(i), inner = profile.InnerAt(i);
        float across = 2f * LatheSection.Reach(r, profile.SquareHalfMm) * M;
        var axis = _workpiece.GlobalBasis.Orthonormalized().X;
        var down = (Vector3.Down - axis * axis.Dot(Vector3.Down)).Normalized();
        return new SawCut(_workpiece.ToGlobal(new Vector3(z * M, 0f, 0f)), axis, down, across, across,
            LatheSection.Area(r, profile.SquareHalfMm) - Mathf.Pi * inner * inner, z);
    }

    /// <summary>The bar's thread for a gauge: the spindle stopped, the ring goes on from the tip.</summary>
    public ThreadSite? ThreadAt(Vector3 globalPoint, out string why)
    {
        why = null;
        var profile = _workpiece.Profile;
        if (profile == null)
            return null;
        if (System.Math.Abs(Bridge.State.SpindleRpm) > 1.0)
        {
            why = "İş mili dönerken mastar takılmaz";
            return null;
        }
        var radii = profile.LiveRadii();
        int index = Mathf.Clamp(profile.IndexAt(_workpiece.ToLocal(globalPoint).X / M), 0, radii.Length - 1);
        return new ThreadSite(radii, profile.StepMm, index, _workpiece.ToGlobal(new Vector3(profile.ZMin * M, 0f, 0f)),
            _workpiece.GlobalBasis.Orthonormalized().X, radii.Length - 1);
    }

    public void FinishCut(SawCut cut) => _cutting.SawThrough(cut.AtMm, BenchVise.KerfMm);

    /// <summary>True diameter of the bar at a machine Z (mm).</summary>
    public float DiameterAt(float machineZ) =>
        _workpiece.Profile == null ? 0f : 2f * _workpiece.Profile.RadiusAt(_workpiece.Profile.IndexAt(machineZ));

    public override void _Process(double delta)
    {
        var s = Bridge.State;
        float rpm = (float)System.Math.Abs(s.SpindleRpm);
        // Signed: M4 turns the other way.
        float spin = Mathf.Sign((float)s.SpindleRpm) * Mathf.Min(rpm * 6f, MaxSpinDegPerSec) * (float)delta;
        Chuck.Spin(spin);
        _workpiece.RotateX(Mathf.DegToRad(spin));
        CheckDoorInterlock();
        CheckKeyLeftIn(Chuck, rpm > 1f, "AYNA ANAHTARI FIRLADI: anahtar aynada unutuldu, iş mili çalıştırıldı");
        CheckChuckSpeed(rpm);
        CheckCams(rpm > 1f);
        UpdateIndicator(rpm > 1f);
    }

    private bool _overSpeedWarned, _camsTripped;

    /// <summary>A chuck not locked to the nose works loose as soon as the
    /// spindle turns.</summary>
    private void CheckCams(bool turning)
    {
        if (turning && Chuck.Mounted && !Chuck.CamsDone && !_camsTripped)
        {
            _camsTripped = true;
            Safety.Trip(IncidentKind.Crash, $"AYNA FENER MİLİNDEN BOŞALDI: kam kilitleri kilitlenmeden iş mili çalıştırıldı ({Chuck.CamsLocked}/{Chuck.CamCount})");
        }
        else if (!turning)
            _camsTripped = false;
    }


    /// <summary>The control only knows the chuck it was set up with; a
    /// slower chuck fitted since is the operator's to respect.</summary>
    private void CheckChuckSpeed(float rpm)
    {
        float limit = Chuck.Mounted ? Chuck.Model.MaxRpm : float.PositiveInfinity;
        bool over = rpm > limit + 1f;
        if (over && !_overSpeedWarned)
            Safety.Warn(System.FormattableString.Invariant(
                $"{Chuck.Model.DisplayName} en fazla {limit:0} dev/dk: iş mili {rpm:0} dev/dk dönüyor (S değerini düşür)"));
        _overSpeedWarned = over;
    }

    // A dial indicator on the bar, read while the chuck is turned by hand.
    private HandTool _indicator;
    private float _indicatorZ, _handTurn, _needleMin, _needleMax;
    private const float HandTurnStep = Mathf.Pi / 18f;

    private bool SpindleStopped => System.Math.Abs(Bridge.State.SpindleRpm) <= 1.0;

    /// <summary>The direction, in the bar's own frame, that faces up now (where the plunger touches).</summary>
    private float TopAngle => Mathf.Pi / 2f - _handTurn;

    private float Needle => Chuck.Grip.Needle(_indicatorZ - _body.JawFrontZMm, TopAngle);

    private void ResetSweep() => _needleMin = _needleMax = Needle;

    private bool UseOnChuck(PlayerController player)
    {
        if (!Chuck.Grip.HasBar || _workpiece.Profile == null)
            return false;
        var kind = player.HeldTool?.Kind;
        if (kind == HandToolKind.DialIndicator && _indicator == null)
        {
            float z = _workpiece.ToLocal(player.LookPoint).X / M;
            if (!SpindleStopped)
                PlayerController.Notify(this, "İş mili dönerken komparatör konmaz");
            else if (z < _body.JawFrontZMm + 2f || z > _workpiece.Profile.ZMin + LiveLengthMm())
                PlayerController.Notify(this, "Komparatörü kütüğün üstüne koy");
            else
            {
                _indicator = (HandTool)player.ReleaseHeld();
                _indicatorZ = z;
                PlaceIndicator();
                ResetSweep();
            }
            return true;
        }
        if (kind == HandToolKind.Mallet && _indicator != null)
        {
            if (Chuck.Grip.Tap(TopAngle) is string what)
                PlayerController.Notify(this, what);
            ResetSweep();
            return true;
        }
        return false;
    }

    private bool TurnByHand(int steps)
    {
        if (_indicator == null || Chuck.Grip.KeyIn || !SpindleStopped)
            return false;
        _handTurn += steps * HandTurnStep;
        Chuck.Spin(Mathf.RadToDeg(steps * HandTurnStep));
        _workpiece.RotateX(steps * HandTurnStep);
        _needleMin = Mathf.Min(_needleMin, Needle);
        _needleMax = Mathf.Max(_needleMax, Needle);
        return true;
    }

    private string IndicatorPrompt(PlayerController player)
    {
        if (!Chuck.Grip.HasBar)
            return null;
        if (_indicator == null)
            return player.HeldTool?.Kind == HandToolKind.DialIndicator ? "[Tık] Komparatörü kütüğün üstüne koy (salgı ölç)" : null;
        return player.HeldTool?.Kind == HandToolKind.Mallet
            ? $"[Tık] İbrenin altına vur (ayna hafif sıkı olmalı: {Chuck.Grip.Torque}/{ChuckGrip.FullTorque})"
            : player.Held == null ? "[Teker] Aynayı elle çevir   [F] Komparatörü al" : null;
    }

    private void PlaceIndicator()
    {
        float r = _workpiece.Profile.RadiusAt(_workpiece.Profile.IndexAt(_indicatorZ)) * M;
        var axis = _workpiece.GlobalPosition + _workpiece.GlobalBasis.X.Normalized() * (_indicatorZ * M);
        _indicator.Freeze = true;
        _indicator.GlobalTransform = new Transform3D(_body.GlobalBasis.Orthonormalized(), axis + Vector3.Up * (r + 0.03f));
    }

    private void UpdateIndicator(bool turning)
    {
        if (_indicator == null)
            return;
        var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (!IsInstanceValid(_indicator) || player?.Held == _indicator || _indicator.Stowed || !Chuck.Grip.HasBar)
        {
            _indicator = null;
            return;
        }
        if (turning)
        {
            var knocked = _indicator;
            _indicator = null;
            knocked.Freeze = false;
            knocked.ApplyCentralImpulse(Vector3.Up * knocked.Mass * 2f);
            Safety.Trip(IncidentKind.KeyLeftIn, "KOMPARATÖR FIRLADI: komparatör kütüğün üstünde unutuldu, iş mili çalıştırıldı");
            return;
        }
        if (player != null && player.GlobalPosition.DistanceTo(_indicator.GlobalPosition) < 3f)
            player.ToolStatus = System.FormattableString.Invariant(
                $"Komparatör: {Needle:+0.000;-0.000} mm   en düşük {_needleMin:+0.000;-0.000}  en yüksek {_needleMax:+0.000;-0.000}  → salgı {_needleMax - _needleMin:0.000} mm (aynadan {_indicatorZ - _body.JawFrontZMm:0} mm)");
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
