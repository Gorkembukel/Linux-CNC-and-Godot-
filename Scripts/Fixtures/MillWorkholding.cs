using Godot;
using CncTrainer.Bridge;
using CncTrainer.Interaction;
using CncTrainer.Material;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer.Fixtures;

/// <summary>
/// Hands-on setup of a mill: the table the vise is bolted to, a cart with
/// the vise and its bolts, and the link between what the player clamped and
/// what the machine cuts. Stock placed in the vise becomes the stock being
/// machined exactly where it sits; cutting it while it is not held firmly
/// throws it out and stops the machine. Child of the machine root.
/// </summary>
public partial class MillWorkholding : Node3D, ISetupSheet
{
    public MillTable Table { get; private set; }

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

        Table = MillTable.Create(body.TableSizeX, body.TableSizeZ, spec.Mill.TableThicknessMm * WorldScale.MmToM, body.SlotZ);
        Table.Position = new Vector3(0f, body.TableTopY, 0f);
        body.WorkEnd.AddChild(Table);

        cutting.IsSecure = () => Table.Vise is Vise vise && vise.BoltsTight && vise.StockClamped;
        cutting.CutWhileLoose = ThrowPart;

        AddToGroup("setup_sheets");
        BuildCart(new Vector3(spec.Mill.BaseWidthMm * WorldScale.MmToM / 2f + 0.7f, 0f, 0.9f));
    }

    private void BuildCart(Vector3 at)
    {
        const float top = 0.75f;
        var cart = new Node3D { Name = "FixtureCart", Position = at };
        AddChild(cart);
        BodyParts.SolidBox(cart, "Top", new Vector3(0.7f, 0.04f, 0.5f), new Vector3(0f, top - 0.02f, 0f), BodyParts.Steel);
        BodyParts.SolidBox(cart, "Frame", new Vector3(0.6f, top - 0.04f, 0.4f), new Vector3(0f, (top - 0.04f) / 2f, 0f), BodyParts.Mat(new Color(0.75f, 0.1f, 0.1f)));

        var vise = Vise.Create();
        vise.Freeze = true;
        cart.AddChild(vise);
        vise.Position = new Vector3(-0.12f, top, 0f);
        for (int i = 0; i < 4; i++)
        {
            var bolt = BoltItem.Create();
            bolt.Freeze = true;
            cart.AddChild(bolt);
            bolt.Position = new Vector3(0.18f + 0.05f * (i % 2), top, -0.06f + 0.1f * (i / 2));
        }
    }

    public override void _Process(double delta)
    {
        var vise = Table.Vise;
        var stock = vise?.Stock;

        if (stock != null && vise.MachinedPart == null)
        {
            var blank = stock.Blank;
            float step = VoxelStock.StepFor(new Vector3(blank.SizeMm.X, blank.SizeMm.Z, blank.SizeMm.Y), _spec.Mill.VoxelStepMm, 6_000_000);
            var visual = new VoxelStockVisual { Name = "Machined", Position = new Vector3(0f, blank.BoundsM.Y / 2f, 0f) };
            stock.AddChild(visual);
            visual.Build(VoxelStock.Block(blank.SizeMm.X, blank.SizeMm.Z, blank.SizeMm.Y, step));
            vise.ShowMachined(visual);
            _removedAtMount = _cutting.RemovedMm3;
            _mountedAt = default;
            _offsetAtPlacement = _bridge.State.WorkOffset;
        }

        if (stock != null)
        {
            // The part shifts a little when it is clamped and when it is seated.
            if (!_cutting.HasStock || stock.GlobalTransform != _mountedAt)
            {
                _mountedAt = stock.GlobalTransform;
                _cutting.MountStock((VoxelStockVisual)vise.MachinedPart);
            }
            if (_cutting.RemovedMm3 > _removedAtMount)
            {
                vise.WasCut = true;
                Table.Dirty = true;
            }
        }
        else if (_cutting.HasStock)
            _cutting.ClearStock();

        CheckJawCrash(vise);
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
            var vise = Table.Vise;
            var state = _bridge.State;
            static string Mark(bool done) => done ? "[x] " : "[ ] ";
            string text = $"AYAR FÖYÜ - {_spec.MachineName}\n";
            text += Mark(vise != null) + "Mengene tablada" + (vise != null && vise.Rotation.X != 0f ? "  (TALAŞ ÜSTÜNE OTURDU)" : "") + "\n";
            if (vise == null)
                return text + "    Mengeneyi arabadan al, tablaya koy";
            int tight = 0;
            foreach (var bolt in vise.Bolts)
                if (bolt.IsTight) tight++;
            text += Mark(vise.BoltsTight) + $"Cıvatalar tam sıkı  {tight}/{vise.Bolts.Count}\n";
            text += Mark(vise.HasParallels) + "Paraleller\n";
            text += Mark(vise.Stock != null) + "Kütük mengenede\n";
            text += Mark(vise.Torque >= 3) + $"Mengene sıkıldı  {vise.Torque}/3\n";
            text += Mark(vise.Seated) + "Çekiçle oturtuldu\n";
            if (vise.Stock != null)
            {
                var delta = state.WorkOffset - _offsetAtPlacement;
                text += Mark(Mathf.Abs(delta.X) > 1e-4f) + "X sıfırı (kenar bulucu)\n";
                text += Mark(Mathf.Abs(delta.Y) > 1e-4f) + "Y sıfırı (kenar bulucu)\n";
                text += Mark(Mathf.Abs(delta.Z) > 1e-4f) + "Z sıfırı (kağıt mastar)\n";
            }
            if (Table.Dirty)
                text += "    Tabla talaşlı: fırçayla temizle\n";
            return text;
        }
    }

    private void ThrowPart()
    {
        var vise = Table.Vise;
        vise.WasCut = true;
        var part = vise.TakeStock();
        _cutting.ClearStock();
        part.Freeze = false;
        part.ApplyCentralImpulse((GlobalTransform.Basis * new Vector3(0.6f, 1.2f, 1.4f)) * part.Mass * 2.5f);
        StopMachine(vise.BoltsTight
            ? "PARÇA FIRLADI: mengene sıkılmadan kesime girildi"
            : "PARÇA FIRLADI: mengene cıvataları tam sıkılmamıştı");
    }

    /// <summary>The cutter running into a vise jaw: what happens when the
    /// stock sits too low in the jaws and the program cuts at jaw height.</summary>
    private void CheckJawCrash(Vise vise)
    {
        if (vise == null || _crashed || _spec.FindTool(_bridge.State.ToolNumber) is not ToolSpec tool)
            return;
        var tip = vise.GlobalTransform.AffineInverse() * (_body.WorkEnd.GlobalTransform * _cutting.ToolInWork(_bridge.State.Position).Origin);
        float radius = tool.DiameterMm / 2f * WorldScale.MmToM;
        float movingFace = Vise.FixedJawFaceZ + vise.OpeningMm * WorldScale.MmToM;
        bool overJaws = Mathf.Abs(tip.X) < Vise.JawWidth / 2f + radius
                        && ((tip.Z < Vise.FixedJawFaceZ + radius && tip.Z > Vise.FixedJawFaceZ - 0.03f - radius)
                            || (tip.Z > movingFace - radius && tip.Z < movingFace + 0.03f + radius));
        if (overJaws && tip.Y < Vise.JawTopY - 0.0005f)
        {
            _crashed = true;
            StopMachine("ÇARPMA: takım mengene çenesine girdi (kütük çenelerin üstüne çıkmıyor, paralel kullan)");
        }
        else if (!overJaws || tip.Y > Vise.JawTopY + 0.005f)
            _crashed = false;
    }

    private void StopMachine(string reason)
    {
        _bridge.StopProgram();
        _bridge.SpindleOff();
        _bridge.MachineOff();
        PlayerController.Notify(this, reason);
    }
}
