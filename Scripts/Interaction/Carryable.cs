using Godot;

namespace CncTrainer.Interaction;

/// <summary>A physical object the player can pick up with F and carry.</summary>
public partial class Carryable : RigidBody3D
{
    public string DisplayName = "";

    public Carryable()
    {
        AddToGroup("pickupable");
    }
}
