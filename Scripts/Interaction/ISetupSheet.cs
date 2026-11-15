using Godot;

namespace CncTrainer.Interaction;

/// <summary>A machine's setup checklist, shown on the HUD while the player
/// stands near it. Implementers join the "setup_sheets" group.</summary>
public interface ISetupSheet
{
    Vector3 SheetPosition { get; }
    string SheetText { get; }
}
