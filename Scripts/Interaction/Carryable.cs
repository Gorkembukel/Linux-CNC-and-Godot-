using Godot;

namespace CncTrainer.Interaction;

/// <summary>A physical object the player can pick up with F and carry.</summary>
public partial class Carryable : RigidBody3D
{
    public string DisplayName = "";

    /// <summary>False while the object is fitted to something.</summary>
    public virtual bool CanPickUp => true;

    /// <summary>Called when the player takes the object into their hands.</summary>
    public virtual void OnPickedUp() { }

    public Carryable()
    {
        AddToGroup("pickupable");
    }
}
