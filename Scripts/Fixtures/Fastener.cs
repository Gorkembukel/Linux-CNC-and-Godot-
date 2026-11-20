using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>
/// One bolt hole of a fixture. Empty until the player brings a bolt, then
/// turned with the mouse wheel while holding the hex key: started by hand,
/// snug, tight. A bolt backed all the way out can be taken out again.
/// </summary>
public partial class Fastener : StaticBody3D, IInteractable
{
    public const int SnugTurns = 6;
    public const int TightTurns = 10;

    /// <summary>Colliders the player's crosshair can hit but nothing bumps into.</summary>
    public const uint InteractionLayer = 1u << 4;

    public bool Present { get; private set; }
    public int Turns { get; private set; }
    public bool IsTight => Present && Turns >= TightTurns;
    public bool IsSnug => Present && Turns >= SnugTurns;

    /// <summary>What comes out when the fastener is taken out again.</summary>
    public System.Func<Carryable> ItemFactory = BoltItem.Create;
    public System.Action Removed;

    private Node3D _visual;

    public override void _Ready()
    {
        CollisionLayer = InteractionLayer;
        CollisionMask = 0;
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.04f, 0.05f, 0.04f) }, Position = new Vector3(0f, 0.02f, 0f) });
        BodyParts.Cylinder(this, "Hole", 0.008f, 0.002f, new Vector3(0f, 0.001f, 0f), Vector3.Up, BodyParts.Mat(new Color(0.02f, 0.02f, 0.02f), 0f, 1f));
        _visual = new Node3D { Name = "Bolt", Visible = false };
        AddChild(_visual);
        BodyParts.Cylinder(_visual, "Head", 0.011f, 0.012f, new Vector3(0f, 0.006f, 0f), Vector3.Up, BodyParts.DarkSteel);
        BodyParts.Cylinder(_visual, "Shank", 0.006f, 0.03f, new Vector3(0f, -0.015f, 0f), Vector3.Up, BodyParts.DarkSteel);
        Refresh();
    }

    private void Refresh()
    {
        _visual.Visible = Present;
        _visual.Position = new Vector3(0f, (TightTurns - Turns) * 0.0015f, 0f);
    }

    public string Prompt(PlayerController player)
    {
        if (!Present)
            return player.Held is BoltItem ? "[Tık] Cıvatayı tak" : "Boş cıvata deliği (T-somun + cıvata getir)";
        string state = IsTight ? "tam sıkı" : IsSnug ? "hafif sıkı" : Turns > 0 ? "gevşek" : "elle başlatıldı";
        if (player.HeldTool?.Kind == HandToolKind.HexKey)
            return $"[Tekerlek] Sık / gevşet   {Turns}/{TightTurns} ({state})";
        return Turns == 0 && player.Held == null ? "[Tık] Cıvatayı çıkar" : $"Cıvata: {state} (T kollu alyan gerekli)";
    }

    public void Use(PlayerController player)
    {
        if (!Present && player.Held is BoltItem)
        {
            player.ReleaseHeld().QueueFree();
            Present = true;
            Turns = 0;
        }
        else if (Present && Turns == 0 && player.Held == null)
        {
            Present = false;
            var bolt = ItemFactory();
            GetTree().CurrentScene.AddChild(bolt);
            player.Give(bolt);
            Removed?.Invoke();
        }
        Refresh();
    }

    public void Turn(PlayerController player, int steps)
    {
        if (!Present || player.HeldTool?.Kind != HandToolKind.HexKey)
            return;
        Turns = Mathf.Clamp(Turns + steps, 0, TightTurns);
        Refresh();
    }

    /// <summary>Fits the fastener, started by hand.</summary>
    public void Fit()
    {
        Present = true;
        Turns = 0;
        Refresh();
    }

    /// <summary>For a save: turns in, or -1 when no bolt is in.</summary>
    public int SavedTurns => Present ? Turns : -1;

    public void Restore(int savedTurns)
    {
        Present = savedTurns >= 0;
        Turns = System.Math.Max(0, savedTurns);
        if (_visual != null)
            Refresh();
    }

    /// <summary>For tests and scripted setups: fits and tightens the bolt.</summary>
    public void SetTight()
    {
        Present = true;
        Turns = TightTurns;
        Refresh();
    }
}
