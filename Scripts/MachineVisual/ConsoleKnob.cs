using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;

namespace CncTrainer;

/// <summary>A rotary control on the console: mode switch, override dial,
/// handwheel. The mouse wheel turns it one detent per notch; a click steps
/// it once clockwise (wrapping around when WrapOnClick).</summary>
[Tool]
public partial class ConsoleKnob : StaticBody3D, IInteractable
{
    public int Count = 2;
    public bool Endless;
    public bool WrapOnClick;
    public float DegreesPerStep = 30f;
    public float StartDegrees;
    public Node3D Rotor;
    public string Caption = "";
    /// <summary>(new index, signed detents turned).</summary>
    public System.Action<int, int> Changed;

    public int Index { get; private set; }

    public void SetIndex(int index)
    {
        Index = index;
        UpdateRotor();
    }

    public string Prompt(PlayerController player) => Caption;

    public void Use(PlayerController player) => Step(1, WrapOnClick);

    public void Turn(PlayerController player, int steps) => Step(steps, false);

    private void Step(int steps, bool wrap)
    {
        int next = Index + steps;
        if (!Endless)
        {
            next = wrap ? ((next % Count) + Count) % Count : Mathf.Clamp(next, 0, Count - 1);
            if (next == Index)
                return;
        }
        Index = next;
        UpdateRotor();
        Changed?.Invoke(Index, steps);
    }

    private void UpdateRotor()
    {
        if (Rotor != null)
            Rotor.RotationDegrees = new Vector3(0f, 0f, -(StartDegrees + Index * DegreesPerStep));
    }
}
