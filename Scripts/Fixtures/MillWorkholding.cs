using System.Linq;
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
/// Hands-on setup of a mill: the table fixtures are set on (or, on an
/// indexer, the chuck), and the link between what the player clamped and
/// what the machine cuts. Stock in a fixture becomes the stock being
/// machined exactly where it sits; cutting it while it is not held firmly
/// throws it out and stops the machine. The fixtures themselves come from a
/// FixtureCart in the workshop.
/// </summary>
public partial class MillWorkholding : MachineSetup, ISawable, IPathObstacle
{
    private const float M = WorldScale.MmToM;

    public MillTable Table { get; private set; }
    /// <summary>Indexer machines: the chuck on the rotary axis.</summary>
    public JawChuck Chuck { get; private set; }
    public IStockFixture Fixture => Chuck != null ? Chuck : Table.Fixture;
    /// <summary>The spindle, as the place the player puts tools in.</summary>
    public ToolSocket Spindle { get; private set; }

    private ProceduralMillBody _body;
    private MillCutting _cutting;
    private Transform3D _mountedAt;
    private Workshop.StockItem _mountedStock;
    private double _removedAtMount;
    private bool _crashed;
    private bool _edgeTouching, _edgeBroken, _probeSignal;
    private AxisVector _offsetAtPlacement;

    public void Setup(MachineSpec spec, ProceduralMillBody body, MillCutting cutting, BridgeClient bridge, MachineSafety safety)
    {
        Init(spec, bridge, safety);
        _body = body;
        _cutting = cutting;
        var m = spec.Mill;

        if (m.Workholding == Workholding.Chuck)
        {
            // On the rotary axis, where the fixed indexer chuck is drawn.
            Chuck = JawChuck.Create(m.StockHeight + 60f, 3, 30f, 30f);
            Chuck.Saw = this;
            Chuck.Position = new Vector3(-m.StockSizeXMm / 2f * M + 0.02f, -m.StockHeight / 2f * M, 0f);
            body.WorkEnd.AddChild(Chuck);
        }
        else
        {
            Table = MillTable.Create(body.TableSizeX, body.TableSizeZ, m.TableThicknessMm * M, body.SlotZ);
            Table.Position = new Vector3(0f, body.TableTopY, 0f);
            body.WorkEnd.AddChild(Table);
        }

        if (m.MagazinePockets > 0 && body.Magazine != null)
        {
            float size = body.MagazineRadius * 2f;
            Spindle = ToolSocket.Create(m.MagazinePockets, turret: true, new Vector3(size, 0.15f, size), new Vector3(0f, -0.04f, 0f), latheTools: false);
            Spindle.PocketOf = number => number - 1;
            body.Magazine.AddChild(Spindle);
        }
        else
        {
            Spindle = ToolSocket.Create(1, turret: false, new Vector3(0.09f, 0.2f, 0.09f), new Vector3(0f, 0.1f, 0f));
            body.ToolEnd.AddChild(Spindle);
        }
        Spindle.ActiveNumber = () => Bridge.State.ToolNumber;
        Spindle.Turning = () => System.Math.Abs(Bridge.State.SpindleRpm) > 1.0;
        Spindle.Refuse = tool =>
            tool.DiameterMm > m.MaxToolDiameterMm ? $"Takım çok büyük: Ø{tool.DiameterMm:0} mm, iş mili en fazla Ø{m.MaxToolDiameterMm:0} mm alır"
            : tool.LengthMm > m.MaxToolLengthMm ? $"Takım çok uzun: {tool.LengthMm:0} mm, en fazla {m.MaxToolLengthMm:0} mm"
            : null;
        cutting.Tools = Spindle;

        AddDoor(body, body.DoorOpening);

        cutting.IsSecure = () => Fixture?.HoldsFirmly ?? true;
        cutting.CutWhileLoose = ThrowPart;
        cutting.CutObserved += SlipUnderCut;
    }

    /// <summary>A part clamped snug but not fully tight creeps along the
    /// feed under the cutting force. Nothing says so: the cut lands off by
    /// that much, and only measuring the part shows it.</summary>
    private void SlipUnderCut(CutSample sample)
    {
        if (Fixture is not { } fixture || fixture.SlipMmPerMm3 <= 0f || fixture.Seat.MachinedPart is not VoxelStockVisual visual)
            return;
        var v = Bridge.State.Velocity;
        var feed = new Vector3((float)v.X, 0f, -(float)v.Y);
        if (feed.LengthSquared() < 1e-6f)
            return;
        float mm = sample.VolumeMm3 * fixture.SlipMmPerMm3;
        visual.GlobalPosition += (_body.WorkEnd.GlobalBasis * feed).Normalized() * mm * M;
        fixture.Seat.Stock.SlipMm += mm;
        _cutting.MountStock(visual);
    }

    public MaterialKind SawMaterial => Fixture?.Seat.Stock?.Blank.Material ?? MaterialKind.Steel;

    /// <summary>A saw cut through the part in the fixture, the spindle stopped.</summary>
    public SawCut? PlanCut(Vector3 globalPoint, float angleDeg, out string why)
    {
        why = null;
        var seat = Fixture?.Seat;
        if (seat?.Stock == null)
            return null;
        if (seat.MachinedPart is not VoxelStockVisual visual)
        {
            why = "Parça henüz bağlanmadı";
            return null;
        }
        if (System.Math.Abs(Bridge.State.SpindleRpm) > 1.0)
        {
            why = "İş mili dönerken testere vurulmaz!";
            return null;
        }
        return VoxelSaw.Plan(visual.Stock, visual.GlobalTransform, globalPoint,
            VoxelSaw.Normal(seat.Stock.GlobalBasis.Orthonormalized().X, angleDeg), angleDeg, 0f);
    }

    /// <summary>The bigger piece stays in the fixture; the other drops.</summary>
    public void FinishCut(SawCut cut)
    {
        if (Fixture?.Seat.MachinedPart is not VoxelStockVisual visual)
            return;
        var (dropped, changed) = VoxelSaw.Split(visual.Stock, visual.GlobalTransform, cut, null);
        if (changed.MaterialRemoved)
            visual.MarkDirty(changed);
        Fixture.Seat.WasCut = true;
        VoxelSaw.Spawn(dropped, visual.GlobalTransform, SawMaterial, GetTree().CurrentScene);
    }

    private Transform3D InWorkEnd(Node3D node) => _body.WorkEnd.GlobalTransform.AffineInverse() * node.GlobalTransform;

    public override void _Process(double delta)
    {
        var fixture = Fixture;
        var seat = fixture?.Seat;
        var stock = seat?.Stock;
        if (stock != null)
            stock.SawnBy = this;

        if (stock != null && stock != _mountedStock && seat.MachinedPart != null)
        {
            _mountedStock = stock;
            _cutting.Material = stock.Blank.Material;
            _cutting.PartFinished?.Invoke();
            _removedAtMount = _cutting.RemovedMm3;
            _mountedAt = default;
            _offsetAtPlacement = Bridge.State.WorkOffset;
        }
        else if (stock != null && seat.MachinedPart == null)
        {
            _mountedStock = stock;
            var blank = stock.Blank;
            bool block = blank.Shape == StockShape.Block;
            var size = block ? new Vector3(blank.SizeMm.X, blank.SizeMm.Z, blank.SizeMm.Y) : new Vector3(blank.LengthMm, blank.DiameterMm, blank.DiameterMm);
            float step = VoxelStock.StepFor(size, Spec.Mill.VoxelStepMm, 6_000_000);
            var visual = new VoxelStockVisual { Name = "Machined", Position = new Vector3(0f, blank.BoundsM.Y / 2f, 0f) };
            stock.AddChild(visual);
            visual.Build(stock.Turned is { } turned ? VoxelStock.Revolved(turned.RadiiMm, turned.InnerMm, turned.StepMm, step, turned.SquareHalfMm)
                : block ? VoxelStock.Block(size.X, size.Y, size.Z, step)
                : VoxelStock.CylinderAlongX(size.X, size.Y, step));
            seat.ShowMachined(visual);
            _cutting.Material = blank.Material;
            _cutting.PartFinished?.Invoke();
            _removedAtMount = _cutting.RemovedMm3;
            _mountedAt = default;
            _offsetAtPlacement = Bridge.State.WorkOffset;
        }

        if (stock != null)
        {
            // The part shifts when it is clamped, seated, tapped or slid;
            // measured against the table, which itself moves on many machines.
            var at = InWorkEnd(stock);
            if (!_cutting.HasStock || !at.IsEqualApprox(_mountedAt))
            {
                _mountedAt = at;
                _cutting.MountStock((VoxelStockVisual)seat.MachinedPart);
            }
            seat.Surface = _cutting.Surface();
            if (_cutting.RemovedMm3 > _removedAtMount)
            {
                seat.WasCut = true;
                if (Table != null)
                    Table.Dirty = true;
            }
        }
        else
        {
            _mountedStock = null;
            if (_cutting.HasStock)
                _cutting.ClearStock();
        }

        CheckDoorInterlock();
        RunToolChanger(delta);
        CheckFixtureCrash();
        if (Chuck != null)
            CheckKeyLeftIn(Chuck, Mathf.Abs(Bridge.State.Velocity.A) > 0.01f, "ÇARPMA: ayna anahtarı aynada unutuldu, döner eksen çalıştı");
        CheckMeasuringTool();
        ShowPaperGap();
    }

    /// <summary>What really sits in the spindle now.</summary>
    private MountedTool InSpindle => Spindle.Active(Spec.Mill.MagazinePockets > 0 ? Bridge.State.ToolNumber : 0);

    private double _changeTimer;
    private int _lastTool = -1;

    /// <summary>With a magazine the change happens by itself after the
    /// changer's time; an empty pocket leaves the spindle empty.</summary>
    private void RunToolChanger(double delta)
    {
        if (Spec.Mill.MagazinePockets <= 0)
            return;
        var s = Bridge.State;
        if (s.ToolChangePending && s.MachineOn)
        {
            _changeTimer += delta;
            if (_changeTimer >= Spec.Mill.ToolChangeSeconds)
            {
                Bridge.ConfirmToolChange();
                _changeTimer = double.NegativeInfinity;
            }
        }
        else
            _changeTimer = 0.0;

        if (s.ToolNumber != _lastTool)
        {
            if (_lastTool >= 0 && s.ToolNumber > 0 && !Spindle.Active(s.ToolNumber).Present)
                Safety.Warn($"T{s.ToolNumber}: magazinin {s.ToolNumber}. cebi boş, iş milinde takım yok");
            _lastTool = s.ToolNumber;
        }
    }

    /// <summary>The measuring tool in the spindle meeting the stock. The
    /// edge finder kicks sideways and snaps half a millimetre further; the
    /// touch probe signals the control (G38 stops on it) and breaks once
    /// its stylus has given all it can.</summary>
    private void CheckMeasuringTool()
    {
        var tool = InSpindle.Spec;
        float overtravel = tool?.Measures == true ? _cutting.MeasuringOvertravelMm : -1f;
        bool touching = overtravel >= 0f;
        bool probe = tool?.Kind == ToolKind.TouchProbe;
        _body.SetToolKick(touching && !probe);
        if (touching && !_edgeTouching && !probe)
            Safety.Warn(System.FormattableString.Invariant(
                $"Kenar bulucu attı: uç kenarda. İş mili ekseni kenardan {tool.DiameterMm / 2f:0.###} mm uzakta"));
        if (probe != _probeSignal || (probe && touching != _edgeTouching))
        {
            _probeSignal = probe;
            Bridge.SetProbeInput(probe && touching);
        }
        _edgeTouching = touching;

        float limit = probe ? Spec.Mill.ProbeOvertravelMm : EdgeFinderOvertravelMm;
        if (overtravel > limit && !_edgeBroken)
        {
            _edgeBroken = true;
            Safety.Trip(IncidentKind.ToolBroken, probe
                ? "PROB KIRILDI: uç parçaya fazla itildi (yanaşmayı G38.2 ile yap, G0/G1 ile değil)"
                : "KENAR BULUCU KIRILDI: kenara fazla bastırıldı (ince adımla yanaş)");
        }
        else if (!touching)
            _edgeBroken = false;
    }

    /// <summary>How far the edge finder's tip can be pushed past the edge
    /// before it snaps (mm).</summary>
    private const float EdgeFinderOvertravelMm = 0.5f;

    /// <summary>Z touch-off by feel: with the paper gauge in hand, the HUD
    /// says whether the paper still slides under the tool.</summary>
    private void ShowPaperGap()
    {
        if (GetTree().GetFirstNodeInGroup("player") is not PlayerController player
            || player.HeldTool?.Kind != HandToolKind.PaperGauge || player.GlobalPosition.DistanceTo(GlobalPosition) > 4f)
            return;
        var tool = InSpindle.Spec;
        if (tool == null || tool.Measures)
            player.ToolStatus = "Kağıt mastar: iş milinde kesici takım yok";
        else if (_cutting.TipGapMm() is not float gap)
            player.ToolStatus = "Kağıt mastar: takım yüzeyden uzak";
        else if (gap < 0.03f)
            player.ToolStatus = "Kağıt mastar: KAĞIT YIRTILDI, takım yüzeye bastı (yukarı çık)";
        else if (gap <= 0.08f)
            player.ToolStatus = "Kağıt mastar: kağıt sıkışıyor  ->  takım ucu yüzeyin 0,05 mm üstünde";
        else
            player.ToolStatus = "Kağıt mastar: kağıt serbest kayıyor (daha in)";
    }

    protected override string SheetBody
    {
        get
        {
            var fixture = Fixture;
            var inSpindle = InSpindle;
            int wanted = Bridge.State.ToolNumber;
            string tools = !inSpindle.Present ? "[ ] İş mili boş: takım arabasından takım tak\n"
                : wanted > 0 && inSpindle.Spec.Number != wanted
                    ? $"[ ] İş milinde T{inSpindle.Spec.Number} var, kontrol T{wanted} sanıyor\n"
                    : $"[x] İş milinde T{inSpindle.Spec.Number} {inSpindle.Spec.Description}\n";
            if (fixture == null)
                return tools + (Spec.AxisDefs().Any(a => a.OnWorkpiece && a.IsRotary)
                    ? "[ ] Bağlama yok\n    Merkezleme plakasını arabadan al,\n    tabla merkezine oturt"
                    : "[ ] Bağlama yok\n    Mengeneyi arabadan al, tablaya koy\n    (ya da kütüğü tablaya koy, pabuçla bağla)");
            string text = tools + fixture.SheetLines;
            float loadKg = ((fixture as RigidBody3D)?.Mass ?? 0f) + (fixture.Seat.Stock?.Mass ?? 0f);
            if (Table != null && loadKg > Spec.Mill.TableMaxLoadKg)
                text += $"    Tabla yükü aşıldı: {loadKg:0} kg, en fazla {Spec.Mill.TableMaxLoadKg:0} kg\n";
            if (fixture.Seat.Occupied)
            {
                var delta = Bridge.State.WorkOffset - _offsetAtPlacement;
                text += Sheet.Mark(Mathf.Abs(delta.X) > 1e-4f) + "X sıfırı\n";
                text += Sheet.Mark(Mathf.Abs(delta.Y) > 1e-4f) + "Y sıfırı\n";
                text += Sheet.Mark(Mathf.Abs(delta.Z) > 1e-4f) + "Z sıfırı (kağıt mastar)\n";
                text += "    Her takım için: kağıtla değdir,\n    TOUCH OFF Z (boy ofseti)\n";
            }
            if (Table != null && Table.Dirty)
                text += "    Tabla talaşlı: fırçayla temizle\n";
            return text;
        }
    }

    private void ThrowPart()
    {
        var fixture = Fixture;
        string reason = fixture.LooseReason;
        fixture.Seat.WasCut = true;
        var part = fixture.TakeStock();
        _cutting.ClearStock();
        Throw(part, reason);
    }

    /// <summary>The tool, or the holder above it, running into the
    /// fixture's own hardware or into the table.</summary>
    private void CheckFixtureCrash()
    {
        string reason = _cutting.MachineLock ? null : CrashAt(Bridge.State.Position);
        if (reason != null && !_crashed)
            Safety.Trip(IncidentKind.Crash, reason);
        _crashed = reason != null;
    }

    /// <summary>Why the spindle's tool, holder or nose would be inside the
    /// fixture or the table with the machine at this position, or null.</summary>
    public string CrashAt(AxisVector machinePosition)
    {
        if (InSpindle is not { Present: true } mounted)
            return null;
        float radius = mounted.Spec.DiameterMm / 2f * M;
        float length = (mounted.Spec.LengthMm + mounted.ErrorMm.Y) * M;
        var tool = _body.WorkEnd.GlobalTransform * _cutting.TipInWork(machinePosition, mounted);
        Vector3 tip = tool.Origin, holder = tool * new Vector3(0f, length, 0f);
        Vector3 nose = tool * new Vector3(0f, length + ToolMeshBuilder.HolderLength, 0f);
        float noseRadius = Spec.Mill.SpindleHousingDiameterMm * 0.375f * M;

        string reason = null;
        if (Fixture is Node3D node and IStockFixture fixture)
        {
            var toFixture = node.GlobalTransform.AffineInverse();
            reason = fixture.ToolCrash(toFixture * tip, radius)
                     ?? (fixture.ToolCrash(toFixture * holder, HolderRadius) != null ? "ÇARPMA: takım tutucu bağlama aparatına çarptı" : null)
                     ?? (fixture.ToolCrash(toFixture * nose, noseRadius) != null ? "ÇARPMA: iş mili burnu bağlama aparatına çarptı" : null);
        }
        if (reason == null && Table != null)
        {
            var toTable = Table.GlobalTransform.AffineInverse();
            reason = Table.ToolCrash(toTable * tip, radius)
                     ?? (Table.ToolCrash(toTable * holder, HolderRadius) != null ? "ÇARPMA: takım tutucu tablaya çarptı" : null)
                     ?? (Table.ToolCrash(toTable * nose, noseRadius) != null ? "ÇARPMA: iş mili burnu tablaya çarptı" : null);
        }
        return reason;
    }

    private const float HolderRadius = 0.026f;
}
