using Godot;
using CncTrainer.Player;

namespace CncTrainer.Interaction;

/// <summary>Something the player can act on by looking at it. The player's
/// crosshair ray finds the nearest ancestor of the hit collider that
/// implements this.</summary>
public interface IInteractable
{
    /// <summary>Hint for the HUD given what the player holds, or null when
    /// there is nothing to do right now.</summary>
    string Prompt(PlayerController player);

    /// <summary>Left click.</summary>
    void Use(PlayerController player) { }

    /// <summary>Mouse wheel, one call per notch; steps is +1 (tighten) or -1.</summary>
    void Turn(PlayerController player, int steps) { }

    public static IInteractable Find(Node node)
    {
        for (; node != null; node = node.GetParent())
            if (node is IInteractable interactable)
                return interactable;
        return null;
    }
}
