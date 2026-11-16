using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Interaction;
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
/// FixtureCart in the workshop. Child of the machine root.
/// </summary>
public partial class MillWorkholding : Node3D, ISetupSheet
{
    private const float M = WorldScale.MmToM;

    public MillTable Table { get; private set; }
    /// <summary>Indexer machines: the chuck on the rotary axis.</summary>
    public JawChuck Chuck { get; private set; }
    public IStockFixture Fixture => Chuck != null ? Chuck : Table.Fixture;

    private MachineSpec _spec;
    private ProceduralMillBody _body;
    private MillCutting _cutting;
    private BridgeClient _bridge;
    private Transform3D _mountedAt;
    private double _removedAtMount;
    private bool _crashed;
    private bool _edgeTouching, _edgeBroken;
    private AxisVector _offsetAtPlacement;

    public Vector3 SheetPosition => GlobalPosition;

    public void Setup(MachineSpec spec, ProceduralMillBody body, MillCutting cutting, BridgeClient bridge)
    {
        _spec = spec;
        _body = body;
        _cutting = cutting;
        _bridge = bridge;
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

        cutting.IsSecure = () => Fixture?.HoldsFirmly ?? true;
        cutting.CutWhileLoose = ThrowPart;
        AddToGroup("setup_sheets");
    }

    private Transform3D InWorkEnd(Node3D node) => _body.WorkEnd.GlobalTransform.AffineInverse() * node.GlobalTransform;

    public override void _Process(double delta)
    {
        var fixture = Fixture;
        var stock = fixture?.Stock;

        if (stock != null && fixture.MachinedPart == null)
        {
            var blank = stock.Blank;
            bool block = blank.Shape == StockShape.Block;
            var size = block ? new Vector3(blank.SizeMm.X, blank.SizeMm.Z, blank.SizeMm.Y) : new Vector3(blank.LengthMm, blank.DiameterMm, blank.DiameterMm);
            float step = VoxelStock.StepFor(size, _spec.Mill.VoxelStepMm, 6_000_000);
            var visual = new VoxelStockVisual { Name = "Machined", Position = new Vector3(0f, blank.BoundsM.Y / 2f, 0f) };
            stock.AddChild(visual);
            visual.Build(block ? VoxelStock.Block(size.X, size.Y, size.Z, step) : VoxelStock.CylinderAlongX(size.X, size.Y, step));
            fixture.ShowMachined(visual);
            fixture.WasCut = false;
            _removedAtMount = _cutting.RemovedMm3;
            _mountedAt = default;
            _offsetAtPlacement = _bridge.State.WorkOffset;
        }

        if (stock != null)
        {
            // The part shifts when it is clamped, seated, tapped or slid;
            // measured against the table, which itself moves on many machines.
            var at = InWorkEnd(stock);
            if (!_cutting.HasStock || !at.IsEqualApprox(_mountedAt))
            {
                _mountedAt = at;
                _cutting.MountStock((VoxelStockVisual)fixture.MachinedPart);
            }
            if (_cutting.RemovedMm3 > _removedAtMount)
            {
                fixture.WasCut = true;
                if (Table != null)
                    Table.Dirty = true;
            }
        }
        else if (_cutting.HasStock)
            _cutting.ClearStock();

        CheckFixtureCrash(fixture);
        CheckChuckKey();
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
        if (touching && !_edgeTouching && _spec.FindTool(_bridge.State.ToolNumber) is ToolSpec finder)
            PlayerController.Notify(this, System.FormattableString.Invariant(
                $"Kenar bulucu attı: uç kenarda. İş mili ekseni kenardan {finder.DiameterMm / 2f:0.###} mm uzakta"));
        _edgeTouching = touching;

        if (overtravel > 0.5f && !_edgeBroken)
        {
            _edgeBroken = true;
            StopMachine("KENAR BULUCU KIRILDI: kenara fazla bastırıldı (ince adımla yanaş)");
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
        var tool = _spec.FindTool(_bridge.State.ToolNumber);
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

    public string SheetText
    {
        get
        {
            var fixture = Fixture;
            var state = _bridge.State;
            string text = $"AYAR FÖYÜ - {_spec.MachineName}\n";
            if (fixture == null)
                return text + (_spec.AxisDefs().Any(a => a.OnWorkpiece && a.IsRotary) ? "[ ] Bağlama yok\n    Merkezleme plakasını arabadan al,\n    tabla merkezine oturt"
                    : "[ ] Bağlama yok\n    Mengeneyi arabadan al, tablaya koy\n    (ya da kütüğü tablaya koy, pabuçla bağla)");
            text += fixture.SheetLines;
            if (fixture.Stock != null)
            {
                var delta = state.WorkOffset - _offsetAtPlacement;
                text += StockHolding.Mark(Mathf.Abs(delta.X) > 1e-4f) + "X sıfırı\n";
                text += StockHolding.Mark(Mathf.Abs(delta.Y) > 1e-4f) + "Y sıfırı\n";
                text += StockHolding.Mark(Mathf.Abs(delta.Z) > 1e-4f) + "Z sıfırı (kağıt mastar)\n";
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
        fixture.WasCut = true;
        var part = fixture.TakeStock();
        _cutting.ClearStock();
        part.Freeze = false;
        part.ApplyCentralImpulse((GlobalTransform.Basis * new Vector3(0.6f, 1.2f, 1.4f)) * part.Mass * 2.5f);
        StopMachine(reason);
    }

    /// <summary>The cutter running into a vise jaw (stock too low in the
    /// jaws, program cutting at jaw height) or into a step clamp.</summary>
    private void CheckFixtureCrash(IStockFixture fixture)
    {
        if (fixture is not Node3D node || _spec.FindTool(_bridge.State.ToolNumber) is not ToolSpec tool)
            return;
        var tip = node.GlobalTransform.AffineInverse() * (_body.WorkEnd.GlobalTransform * _cutting.ToolInWork(_bridge.State.Position).Origin);
        float radius = tool.DiameterMm / 2f * M;
        bool hit = false, clear = true;
        string reason = null;
        if (fixture is Vise vise)
        {
            float movingFace = Vise.FixedJawFaceZ + vise.OpeningMm * M;
            bool overJaws = Mathf.Abs(tip.X) < Vise.JawWidth / 2f + radius
                            && ((tip.Z < Vise.FixedJawFaceZ + radius && tip.Z > Vise.FixedJawFaceZ - 0.03f - radius)
                                || (tip.Z > movingFace - radius && tip.Z < movingFace + 0.03f + radius));
            hit = overJaws && tip.Y < Vise.JawTopY - 0.0005f;
            clear = !overJaws || tip.Y > Vise.JawTopY + 0.005f;
            reason = "ÇARPMA: takım mengene çenesine girdi (kütük çenelerin üstüne çıkmıyor, paralel kullan)";
        }
        else if (fixture is TableClamping clamping)
        {
            hit = clamping.ToolHits(tip, radius);
            clear = !hit;
            reason = "ÇARPMA: takım pabuca girdi (pabuçları takım yolunun dışına bağla)";
        }

        if (hit && !_crashed)
        {
            _crashed = true;
            StopMachine(reason);
        }
        else if (clear)
            _crashed = false;
    }

    /// <summary>A key left in the indexer chuck hits the table as soon as
    /// the axis turns.</summary>
    private void CheckChuckKey()
    {
        if (Chuck == null || !Chuck.Grip.KeyIn || Mathf.Abs(_bridge.State.Velocity.A) < 0.01f)
            return;
        var key = Chuck.TakeKey();
        key.ApplyCentralImpulse(GlobalTransform.Basis * new Vector3(0.3f, 1.5f, 2f) * key.Mass * 3f);
        StopMachine("ÇARPMA: ayna anahtarı aynada unutuldu, döner eksen çalıştı");
    }

    private void StopMachine(string reason)
    {
        _bridge.StopProgram();
        _bridge.SpindleOff();
        _bridge.MachineOff();
        PlayerController.Notify(this, reason);
    }
}
