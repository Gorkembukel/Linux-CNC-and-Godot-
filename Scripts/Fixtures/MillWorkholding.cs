using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Interaction;
using CncTrainer.Machining;
using CncTrainer.Material;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer.Fixtures;

/// <summary>
/// Hands-on setup of a mill: the table fixtures are set on (or, on an
/// indexer, the chuck), and the link between what the player clamped and
/// what the machine cuts. Stock in a fixture becomes the stock being
/// machined exactly where it sits; cutting it while it is not held firmly
/// throws it out and stops the machine. The fixtures themselves come from a
/// FixtureCart in the workshop.
/// </summary>
public partial class MillWorkholding : MachineSetup
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
    private double _removedAtMount;
    private bool _crashed;
    private bool _edgeTouching, _edgeBroken;
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
            Chuck.Position = new Vector3(-m.StockSizeXMm / 2f * M + 0.02f, -m.StockHeight / 2f * M, 0f);
            body.WorkEnd.AddChild(Chuck);
        }
        else
        {
            Table = MillTable.Create(body.TableSizeX, body.TableSizeZ, m.TableThicknessMm * M, body.SlotZ);
            Table.Position = new Vector3(0f, body.TableTopY, 0f);
            body.WorkEnd.AddChild(Table);
        }

        Spindle = ToolSocket.Create(1, turret: false, new Vector3(0.09f, 0.2f, 0.09f), new Vector3(0f, 0.1f, 0f));
        Spindle.ActiveNumber = () => Bridge.State.ToolNumber;
        Spindle.Turning = () => System.Math.Abs(Bridge.State.SpindleRpm) > 1.0;
        body.ToolEnd.AddChild(Spindle);
        cutting.Tools = Spindle;

        AddDoor(body, body.DoorOpening);

        cutting.IsSecure = () => Fixture?.HoldsFirmly ?? true;
        cutting.CutWhileLoose = ThrowPart;
    }

    private Transform3D InWorkEnd(Node3D node) => _body.WorkEnd.GlobalTransform.AffineInverse() * node.GlobalTransform;

    public override void _Process(double delta)
    {
        var fixture = Fixture;
        var seat = fixture?.Seat;
        var stock = seat?.Stock;

        if (stock != null && seat.MachinedPart == null)
        {
            var blank = stock.Blank;
            bool block = blank.Shape == StockShape.Block;
            var size = block ? new Vector3(blank.SizeMm.X, blank.SizeMm.Z, blank.SizeMm.Y) : new Vector3(blank.LengthMm, blank.DiameterMm, blank.DiameterMm);
            float step = VoxelStock.StepFor(size, Spec.Mill.VoxelStepMm, 6_000_000);
            var visual = new VoxelStockVisual { Name = "Machined", Position = new Vector3(0f, blank.BoundsM.Y / 2f, 0f) };
            stock.AddChild(visual);
            visual.Build(block ? VoxelStock.Block(size.X, size.Y, size.Z, step) : VoxelStock.CylinderAlongX(size.X, size.Y, step));
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
            seat.SurfaceNote = _cutting.SurfaceNote();
            if (_cutting.RemovedMm3 > _removedAtMount)
            {
                seat.WasCut = true;
                if (Table != null)
                    Table.Dirty = true;
            }
        }
        else if (_cutting.HasStock)
            _cutting.ClearStock();

        CheckDoorInterlock();
        CheckFixtureCrash(fixture);
        if (Chuck != null)
            CheckKeyLeftIn(Chuck, Mathf.Abs(Bridge.State.Velocity.A) > 0.01f, "ÇARPMA: ayna anahtarı aynada unutuldu, döner eksen çalıştı");
        CheckEdgeFinder();
        ShowPaperGap();
    }

    /// <summary>The edge finder kicks sideways the moment it reaches the
    /// stock; pushed half a millimetre further it snaps.</summary>
    private void CheckEdgeFinder()
    {
        float overtravel = _cutting.EdgeFinderOvertravelMm;
        bool touching = overtravel >= 0f;
        _body.SetToolKick(touching);
        if (touching && !_edgeTouching && Spindle.Active(0).Spec is ToolSpec finder)
            Safety.Warn(System.FormattableString.Invariant(
                $"Kenar bulucu attı: uç kenarda. İş mili ekseni kenardan {finder.DiameterMm / 2f:0.###} mm uzakta"));
        _edgeTouching = touching;

        if (overtravel > 0.5f && !_edgeBroken)
        {
            _edgeBroken = true;
            Safety.Trip(IncidentKind.ToolBroken, "KENAR BULUCU KIRILDI: kenara fazla bastırıldı (ince adımla yanaş)");
        }
        else if (!touching)
            _edgeBroken = false;
    }

    /// <summary>Z touch-off by feel: with the paper gauge in hand, the HUD
    /// says whether the paper still slides under the tool.</summary>
    private void ShowPaperGap()
    {
        if (GetTree().GetFirstNodeInGroup("player") is not PlayerController player
            || player.HeldTool?.Kind != HandToolKind.PaperGauge || player.GlobalPosition.DistanceTo(GlobalPosition) > 4f)
            return;
        var tool = Spindle.Active(0).Spec;
        if (tool == null || tool.Kind == ToolKind.EdgeFinder)
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
            var inSpindle = Spindle.Active(0);
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
    private void CheckFixtureCrash(IStockFixture fixture)
    {
        string reason = null;
        if (Spindle.Active(0) is { Present: true } mounted)
        {
            float radius = mounted.Spec.DiameterMm / 2f * M;
            float length = (mounted.Spec.LengthMm + mounted.ErrorMm.Y) * M;
            var tool = _body.WorkEnd.GlobalTransform * _cutting.ToolInWork(Bridge.State.Position);
            Vector3 tip = tool * new Vector3(0f, -mounted.ErrorMm.Y * M, 0f), holder = tool * new Vector3(0f, length, 0f);

            if (fixture is Node3D node)
            {
                var toFixture = node.GlobalTransform.AffineInverse();
                reason = fixture.ToolCrash(toFixture * tip, radius)
                         ?? (fixture.ToolCrash(toFixture * holder, HolderRadius) != null ? "ÇARPMA: takım tutucu bağlama aparatına çarptı" : null);
            }
            if (reason == null && Table != null)
                reason = Table.ToolCrash(Table.GlobalTransform.AffineInverse() * tip, radius);
        }
        if (reason != null && !_crashed)
            Safety.Trip(IncidentKind.Crash, reason);
        _crashed = reason != null;
    }

    private const float HolderRadius = 0.026f;
}
