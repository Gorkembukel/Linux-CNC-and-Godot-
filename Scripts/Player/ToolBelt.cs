using Godot;
using CncTrainer.Fixtures;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Player;

/// <summary>
/// The player's tool belt: eight holsters in an arc across the front of the
/// waist, where looking down reaches every one. A hand tool is taken from a
/// holster and put back into one by looking at it (PlayerController, F).
/// It turns with the player's body but not with the head. Holsters and the
/// tools in them are only in the interaction layer: the crosshair finds
/// them, nothing bumps into them.
/// </summary>
public partial class ToolBelt : Node3D
{
    public const int SlotCount = 8;
    /// <summary>Across the front of the waist, so looking down reaches the outer ones.</summary>
    private const float ArcDeg = 120f;
    private const float Radius = 0.34f;

    private readonly HandTool[] _tools = new HandTool[SlotCount];
    private readonly (uint Layer, uint Mask)[] _layers = new (uint, uint)[SlotCount];
    private readonly Node3D[] _slots = new Node3D[SlotCount];
    private readonly StaticBody3D[] _holsters = new StaticBody3D[SlotCount];

    public override void _Ready()
    {
        var leather = BodyParts.Mat(new Color(0.32f, 0.2f, 0.12f), 0f, 0.85f);
        var buckle = BodyParts.Mat(new Color(0.7f, 0.65f, 0.5f), 0.8f, 0.35f);
        // Only the front of the band: in first person there is no body for the rest to wrap.
        const int bandPieces = 18;
        for (int k = 0; k <= 9; k++)
        {
            float angle = Mathf.DegToRad(-90f + 180f * k / 9f);
            var piece = BodyParts.Box(this, "Band", new Vector3(2f * Mathf.Pi * (Radius - 0.03f) / bandPieces + 0.01f, 0.05f, 0.01f),
                Vector3.Zero, leather);
            piece.Transform = new Transform3D(new Basis(Vector3.Up, angle), new Basis(Vector3.Up, angle) * new Vector3(0f, 0f, -(Radius - 0.03f)));
        }
        BodyParts.Box(this, "Buckle", new Vector3(0.06f, 0.045f, 0.012f), new Vector3(0f, 0f, -(Radius - 0.02f)), buckle);

        for (int i = 0; i < SlotCount; i++)
        {
            var slot = new Node3D { Name = $"Slot{i + 1}", Transform = SlotTransform(i) };
            AddChild(slot);
            _slots[i] = slot;
            BodyParts.Box(slot, "Holster", new Vector3(0.05f, 0.07f, 0.03f), new Vector3(0f, -0.02f, 0f), leather);
            var holster = new StaticBody3D { Name = "Hit", CollisionLayer = Fastener.InteractionLayer, CollisionMask = 0 };
            holster.SetMeta("belt_slot", i);
            holster.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.07f, 0.1f, 0.06f) }, Position = new Vector3(0f, -0.02f, 0f) });
            slot.AddChild(holster);
            _holsters[i] = holster;
        }
    }

    /// <summary>Slot 1 on the left hip, 8 on the right, facing out.</summary>
    private static Transform3D SlotTransform(int i)
    {
        float angle = Mathf.DegToRad(ArcDeg / 2f - ArcDeg * i / (SlotCount - 1));
        var basis = new Basis(Vector3.Up, angle);
        return new Transform3D(basis, basis * new Vector3(0f, 0f, -Radius));
    }

    /// <summary>The slot a crosshair hit belongs to (its holster or the
    /// tool in it), or -1.</summary>
    public int SlotOf(Node hit)
    {
        if (hit == null)
            return -1;
        if (hit.HasMeta("belt_slot"))
            return hit.GetMeta("belt_slot").AsInt32();
        for (int i = 0; i < SlotCount; i++)
            if (_tools[i] != null && _tools[i] == hit)
                return i;
        return -1;
    }

    /// <summary>Adds the holsters and the tools in them to a ray's exclusions.</summary>
    public void Exclude(Godot.Collections.Array<Rid> rids)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            rids.Add(_holsters[i].GetRid());
            if (_tools[i] != null)
                rids.Add(_tools[i].GetRid());
        }
    }

    public HandTool At(int slot) => _tools[slot];

    /// <summary>Puts a tool into an empty holster.</summary>
    public void Stow(int slot, HandTool tool)
    {
        _tools[slot] = tool;
        _layers[slot] = (tool.CollisionLayer, tool.CollisionMask);
        tool.Freeze = true;
        tool.LinearVelocity = tool.AngularVelocity = Vector3.Zero;
        tool.CollisionLayer = Fastener.InteractionLayer;
        tool.CollisionMask = 0;
        tool.Stowed = true;
        Place(slot);
    }

    /// <summary>Takes the tool out of a holster (null if it is empty).</summary>
    public HandTool Take(int slot)
    {
        var tool = _tools[slot];
        if (tool == null)
            return null;
        _tools[slot] = null;
        (tool.CollisionLayer, tool.CollisionMask) = _layers[slot];
        tool.Stowed = false;
        return tool;
    }

    // A tool sits in its holster sticking out and down (its length, local X,
    // 40 deg below the outward direction), so from above its length shows.
    private static readonly Basis Hanging = new Basis(Vector3.Up, Mathf.Pi / 2f) * new Basis(Vector3.Back, -Mathf.DegToRad(40f));

    private void Place(int slot)
    {
        var at = _slots[slot].GlobalTransform;
        _tools[slot].GlobalTransform = new Transform3D(at.Basis.Orthonormalized() * Hanging, at.Origin + at.Basis.Orthonormalized() * Hanging * new Vector3(0.06f, 0f, 0f));
    }

    public override void _PhysicsProcess(double delta)
    {
        for (int i = 0; i < SlotCount; i++)
            if (_tools[i] != null)
                Place(i);
    }
}
