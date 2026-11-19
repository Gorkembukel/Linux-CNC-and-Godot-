using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Fixtures;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;

namespace CncTrainer.Workshop;

/// <summary>
/// The chuck cabinet: a glass-doored case above with chucks of every size
/// on its shelves, each with its hard jaws in; a drawer unit below with a
/// drawer per chuck holding its spare jaw sets (or its collets); a worktop
/// with a chuck stand between, where a chuck's jaws are changed. Which
/// lathe takes which chuck is the lathe's business (spindle nose, size).
/// Everything inside lies still (frozen) until taken.
/// </summary>
[GlobalClass]
public partial class ChuckCabinet : StaticBody3D
{
    /// <summary>The chucks it holds; empty = the standard catalogue
    /// (scroll chucks Ø80-Ø250, four-jaw independents, collet chucks).</summary>
    [Export] public Godot.Collections.Array<ChuckModel> Chucks = new();
    /// <summary>Spindle bore depth the chucks' bars may go into (mm), as on
    /// the lathes they go on.</summary>
    [Export] public float BoreDepthMm = 300f;

    private const float Width = 1.3f, Depth = 0.6f, DrawerHeight = 0.9f, Top = 0.04f;
    /// <summary>The worktop reaches past the drawer unit to the front and the
    /// sides, so a chuck stand and the parts around it have room.</summary>
    private const float TopWidth = 1.7f, TopDepth = 0.95f;
    private const float TopFrontZ = Depth / 2f + 0.3f;
    /// <summary>The drawer unit runs from the back to just behind the
    /// worktop's front edge: its drawer fronts line up under it.</summary>
    private const float UnitBackZ = -Depth / 2f, UnitFrontZ = TopFrontZ - 0.02f;
    private const float UnitDepth = UnitFrontZ - UnitBackZ, UnitMidZ = (UnitFrontZ + UnitBackZ) / 2f;
    private const float CaseBottom = 1.25f, CaseHeight = 0.8f, CaseDepth = 0.4f;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;
        var models = Chucks.Where(c => c != null).ToArray();
        if (models.Length == 0)
            models = ChuckModel.Catalogue();
        BuildCarcass();
        var stand = ChuckStand.Create();
        AddChild(stand);
        stand.Position = new Vector3(-TopWidth * 0.25f, DrawerHeight + Top, TopFrontZ - 0.3f);
        stand.FixInPlace();
        BuildCase(models);
        BuildDrawers(models);
    }

    private void BuildCarcass()
    {
        var body = BodyParts.Mat(new Color(0.32f, 0.36f, 0.42f), 0.3f, 0.5f);
        var top = BodyParts.Mat(new Color(0.55f, 0.42f, 0.28f), 0f, 0.7f);
        // Drawer unit sides, back and worktop.
        foreach (float side in new[] { -1f, 1f })
            BodyParts.SolidBox(this, "Side", new Vector3(0.02f, DrawerHeight, UnitDepth), new Vector3(side * (Width / 2f - 0.01f), DrawerHeight / 2f, UnitMidZ), body);
        BodyParts.SolidBox(this, "Back", new Vector3(Width, DrawerHeight, 0.02f), new Vector3(0f, DrawerHeight / 2f, -Depth / 2f + 0.01f), body);
        BodyParts.SolidBox(this, "Divider", new Vector3(0.02f, DrawerHeight, UnitDepth), new Vector3(0f, DrawerHeight / 2f, UnitMidZ), body);
        BodyParts.SolidBox(this, "Worktop", new Vector3(TopWidth, Top, TopDepth), new Vector3(0f, DrawerHeight + Top / 2f, TopFrontZ - TopDepth / 2f), top);
        // Legs under the overhanging front corners.
        foreach (float side in new[] { -1f, 1f })
            BodyParts.SolidBox(this, "Leg", new Vector3(0.04f, DrawerHeight, 0.04f), new Vector3(side * (TopWidth / 2f - 0.03f), DrawerHeight / 2f, TopFrontZ - 0.03f), body);
        // Uprights carrying the glass case at the back of the worktop.
        foreach (float side in new[] { -1f, 1f })
            BodyParts.SolidBox(this, "Upright", new Vector3(0.04f, CaseBottom - DrawerHeight - Top, 0.04f),
                new Vector3(side * (Width / 2f - 0.02f), (CaseBottom + DrawerHeight + Top) / 2f, -Depth / 2f + 0.02f), body);
    }

    private float CaseZ => -Depth / 2f + CaseDepth / 2f;

    private void BuildCase(ChuckModel[] models)
    {
        var body = BodyParts.Mat(new Color(0.32f, 0.36f, 0.42f), 0.3f, 0.5f);
        var shelfMat = BodyParts.Mat(new Color(0.45f, 0.47f, 0.5f), 0.4f, 0.5f);
        float z = CaseZ, mid = CaseBottom + CaseHeight / 2f;
        foreach (float side in new[] { -1f, 1f })
            BodyParts.SolidBox(this, "CaseSide", new Vector3(0.02f, CaseHeight, CaseDepth), new Vector3(side * (Width / 2f - 0.01f), mid, z), body);
        BodyParts.SolidBox(this, "CaseBack", new Vector3(Width, CaseHeight, 0.02f), new Vector3(0f, mid, -Depth / 2f + 0.01f), body);
        BodyParts.SolidBox(this, "CaseTop", new Vector3(Width, 0.02f, CaseDepth), new Vector3(0f, CaseBottom + CaseHeight - 0.01f, z), body);
        float[] shelves = { CaseBottom + 0.01f, CaseBottom + CaseHeight * 0.5f };
        foreach (float y in shelves)
            BodyParts.SolidBox(this, "Shelf", new Vector3(Width - 0.04f, 0.02f, CaseDepth - 0.02f), new Vector3(0f, y, z), shelfMat);

        // Chucks along the shelves, largest first, each standing on its back.
        int shelf = 0;
        float x = -Width / 2f + 0.04f;
        foreach (var model in models.OrderByDescending(m => m.DiameterMm))
        {
            float d = model.DiameterMm * WorldScale.MmToM;
            if (x + d > Width / 2f - 0.04f && shelf < shelves.Length - 1)
            {
                shelf++;
                x = -Width / 2f + 0.04f;
            }
            var chuck = ChuckItem.Create(model, new ChuckGrip(model.Dims(BoreDepthMm)));
            chuck.Freeze = true;
            AddChild(chuck);
            chuck.Position = new Vector3(x + d / 2f, shelves[shelf] + 0.01f, z + 0.02f);
            x += d + 0.025f;
        }

        // Two glass doors hinged at the outer edges.
        foreach (float side in new[] { -1f, 1f })
            AddChild(CabinetDoor.Create(side, Width / 2f, CaseHeight - 0.02f, new Vector3(side * (Width / 2f), mid, z + CaseDepth / 2f)));
    }

    private void BuildDrawers(ChuckModel[] models)
    {
        int rows = Mathf.Max(1, Mathf.CeilToInt(models.Length / 2f));
        float h = (DrawerHeight - 0.04f) / rows;
        for (int i = 0; i < models.Length; i++)
        {
            int column = i % 2, row = i / 2;
            var model = models.OrderBy(m => m.Kind).ThenBy(m => m.DiameterMm).ElementAt(i);
            float x = (column == 0 ? -1f : 1f) * Width / 4f;
            float y = DrawerHeight - 0.02f - h * (row + 0.5f);
            var drawer = CabinetDrawer.Create(Width / 2f - 0.04f, h - 0.012f, UnitDepth - 0.04f, Label(model));
            // Placed before it enters the tree: it slides out from here.
            drawer.Position = new Vector3(x, y, UnitMidZ + 0.01f);
            AddChild(drawer);
            Fill(drawer, model);
        }
    }

    private static string Label(ChuckModel model) => model.Kind == ChuckKind.Collet
        ? $"{model.DisplayName}: pensler"
        : $"{model.DisplayName}: çeneler ({SpindleNoses.NameOf(model.Mount)})";

    /// <summary>A drawer's spare jaw sets (hard, reversed, soft; four-jaw:
    /// hard and soft) or collets, laid out in rows in its tray.</summary>
    private static void Fill(CabinetDrawer drawer, ChuckModel model)
    {
        var items = new List<Carryable>();
        if (model.Kind == ChuckKind.Collet)
            items.AddRange(model.ColletSizesMm.Select(size => (Carryable)ColletItem.Create(size)));
        else
        {
            var kinds = model.Kind == ChuckKind.FourJawIndependent
                ? new[] { JawKind.Hard, JawKind.Soft }
                : new[] { JawKind.Hard, JawKind.HardReversed, JawKind.Soft };
            foreach (var kind in kinds)
                for (int n = 1; n <= model.JawCount; n++)
                    items.Add(JawItem.Create(kind, n, model));
        }
        int perRow = model.Kind == ChuckKind.Collet ? 6 : model.JawCount;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            item.Freeze = true;
            drawer.Tray.AddChild(item);
            float across = drawer.TrayWidth / (perRow + 1);
            float along = drawer.TrayDepth / ((items.Count + perRow - 1) / perRow + 1);
            item.Position = new Vector3(-drawer.TrayWidth / 2f + across * (i % perRow + 1), 0.006f, -drawer.TrayDepth / 2f + along * (i / perRow + 1));
            // Jaws lie along the drawer, tongue down.
            item.RotationDegrees = new Vector3(0f, 90f, 0f);
        }
    }
}

/// <summary>A glass case door on a hinge: a click swings it open or shut.</summary>
public partial class CabinetDoor : Node3D
{
    private StaticBody3D _leaf;
    private bool _open;
    private float _side;

    public static CabinetDoor Create(float side, float width, float height, Vector3 hinge)
    {
        var door = new CabinetDoor { Name = "Door", Position = hinge, _side = side };
        var leaf = new DoorLeaf { Name = "Leaf", Door = door, CollisionLayer = Fastener.InteractionLayer | 1, CollisionMask = 0 };
        door.AddChild(leaf);
        door._leaf = leaf;
        var frame = BodyParts.Mat(new Color(0.25f, 0.28f, 0.33f), 0.3f, 0.5f);
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.75f, 0.85f, 0.95f, 0.18f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.05f, Metallic = 0.1f,
        };
        float cx = -side * width / 2f, bar = 0.035f;
        foreach (float y in new[] { -height / 2f + bar / 2f, height / 2f - bar / 2f })
            BodyParts.Box(leaf, "Rail", new Vector3(width, bar, 0.02f), new Vector3(cx, y, 0f), frame);
        foreach (float x in new[] { cx - width / 2f + bar / 2f, cx + width / 2f - bar / 2f })
            BodyParts.Box(leaf, "Stile", new Vector3(bar, height, 0.02f), new Vector3(x, 0f, 0f), frame);
        BodyParts.Box(leaf, "Glass", new Vector3(width - 2f * bar, height - 2f * bar, 0.004f), new Vector3(cx, 0f, 0f), glass);
        BodyParts.Cylinder(leaf, "Knob", 0.01f, 0.03f, new Vector3(cx - side * (width / 2f - 0.06f), 0f, 0.02f), Vector3.Back, BodyParts.Steel);
        leaf.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(width, height, 0.02f) }, Position = new Vector3(cx, 0f, 0f) });
        return door;
    }

    public string Prompt => _open ? "[Tık] Kapağı kapat" : "[Tık] Kapağı aç";

    public void Toggle()
    {
        _open = !_open;
        CreateTween().TweenProperty(this, "rotation:y", _open ? _side * Mathf.DegToRad(105f) : 0f, 0.4)
            .SetTrans(Tween.TransitionType.Sine);
    }

    private partial class DoorLeaf : StaticBody3D, IInteractable
    {
        public CabinetDoor Door;
        public string Prompt(PlayerController player) => player.Held == null ? Door.Prompt : null;
        public void Use(PlayerController player)
        {
            if (player.Held == null)
                Door.Toggle();
        }
    }
}

/// <summary>A drawer: a click pulls it out or pushes it in; what lies in its
/// tray goes with it.</summary>
public partial class CabinetDrawer : StaticBody3D, IInteractable
{
    public Node3D Tray { get; private set; }
    public float TrayWidth { get; private set; }
    public float TrayDepth { get; private set; }
    private string _label;
    private bool _open;
    private Vector3 _shut;

    public static CabinetDrawer Create(float width, float height, float depth, string label)
    {
        var drawer = new CabinetDrawer
        {
            Name = "Drawer", _label = label, TrayWidth = width - 0.03f, TrayDepth = depth - 0.03f,
            CollisionLayer = Fastener.InteractionLayer | 1, CollisionMask = 0,
        };
        var front = BodyParts.Mat(new Color(0.36f, 0.4f, 0.47f), 0.3f, 0.45f);
        var tray = BodyParts.Mat(new Color(0.28f, 0.3f, 0.34f), 0.2f, 0.6f);
        float frontZ = depth / 2f;
        BodyParts.Box(drawer, "Front", new Vector3(width, height, 0.018f), new Vector3(0f, 0f, frontZ), front);
        BodyParts.Box(drawer, "Handle", new Vector3(width * 0.35f, 0.012f, 0.025f), new Vector3(0f, height * 0.2f, frontZ + 0.02f), BodyParts.Steel);
        drawer.AddChild(new Label3D
        {
            Text = label, FontSize = 32, PixelSize = 0.00045f, Modulate = Colors.White, OutlineSize = 6,
            Position = new Vector3(0f, -height * 0.18f, frontZ + 0.0095f), Width = width / 0.00045f * 0.95f,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        // The tray: a floor and low walls.
        float floorY = -height / 2f + 0.01f;
        BodyParts.Box(drawer, "Floor", new Vector3(width - 0.01f, 0.006f, depth), new Vector3(0f, floorY, 0f), tray);
        foreach (float side in new[] { -1f, 1f })
            BodyParts.Box(drawer, "Wall", new Vector3(0.008f, height * 0.6f, depth), new Vector3(side * (width / 2f - 0.01f), floorY + height * 0.3f, 0f), tray);
        BodyParts.Box(drawer, "Rear", new Vector3(width - 0.01f, height * 0.6f, 0.008f), new Vector3(0f, floorY + height * 0.3f, -depth / 2f), tray);
        drawer.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(width, height, 0.02f) }, Position = new Vector3(0f, 0f, frontZ) });
        drawer.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(width, 0.006f, depth) }, Position = new Vector3(0f, floorY, 0f) });
        drawer.Tray = new Node3D { Name = "Tray", Position = new Vector3(0f, floorY + 0.003f, 0f) };
        drawer.AddChild(drawer.Tray);
        return drawer;
    }

    public override void _Ready() => _shut = Position;

    public string Prompt(PlayerController player) =>
        player.Held != null ? null : $"{_label}   [Tık] Çekmeceyi {(_open ? "kapat" : "aç")}";

    public void Use(PlayerController player)
    {
        if (player.Held != null)
            return;
        _open = !_open;
        CreateTween().TweenProperty(this, "position", _shut + (_open ? Vector3.Back * Mathf.Min(TrayDepth * 0.8f, 0.5f) : Vector3.Zero), 0.35)
            .SetTrans(Tween.TransitionType.Sine);
    }
}
