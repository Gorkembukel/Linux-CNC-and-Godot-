using Godot;
using CncTrainer.Interaction;

namespace CncTrainer.Fixtures;

/// <summary>A chuck off the lathe: its own sizes (<see cref="ChuckModel"/>)
/// and its own state (<see cref="ChuckGrip"/>: the jaws or collet in it go
/// with it), heavy as it is. Clicked onto a lathe's chuck once that one's
/// cam locks are undone, it takes its place; set face up on a chuck stand,
/// its jaws can be changed off the machine.</summary>
public partial class ChuckItem : Carryable, Save.ISavedItem
{
    public Godot.Collections.Dictionary SaveState() => new() { ["model"] = Save.SaveCodec.Resource(Model), ["chuck"] = Save.SaveCodec.Chuck(Grip) };

    public static ChuckItem Restore(Godot.Collections.Dictionary d) =>
        Create(Save.SaveCodec.Resource<ChuckModel>(d["model"].AsGodotDictionary()), Save.SaveCodec.Chuck(d["chuck"].AsGodotDictionary()));

    public ChuckModel Model { get; private set; }
    public ChuckGrip Grip { get; private set; }
    public ChuckVisual Visual { get; private set; }
    /// <summary>Lying on a chuck stand: lifted from there, not picked up.</summary>
    public bool Seated;

    public override bool CanPickUp => !Seated;

    public static ChuckItem Create(ChuckModel model, ChuckGrip grip)
    {
        var item = new ChuckItem { Model = model, Grip = grip, Name = "Chuck", DisplayName = model.DisplayName, Mass = model.MassKg };
        item.Visual = ChuckVisual.Create(model);
        item.AddChild(item.Visual);
        item.Visual.ShowGrip(grip);
        // The body up to its face: a jaw laid on the face rests on it.
        float radius = item.Visual.RadiusM, height = item.Visual.FaceM;
        item._plain = new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = height }, Position = new Vector3(0f, height / 2f, 0f) };
        item.AddChild(item._plain);
        return item;
    }

    private CollisionShape3D _plain;

    /// <summary>On a stand its slots are real: jaws go down and slide in
    /// them. Carried about it is a plain cylinder. A collet chuck has no
    /// slots and keeps its cylinder.</summary>
    public void SetSlotCollision(bool slots)
    {
        bool cut = slots && Model.Kind != ChuckKind.Collet;
        Visual.SetSlotCollision(cut);
        _plain.Disabled = cut;
    }

    /// <summary>Redraws the jaws after the grip changed.</summary>
    public void Refresh() => Visual.ShowGrip(Grip);
}
