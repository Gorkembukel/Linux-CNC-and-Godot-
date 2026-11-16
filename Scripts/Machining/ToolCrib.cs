using System.Collections.Generic;

namespace CncTrainer.Machining;

/// <summary>Condition of one tool of a machine.</summary>
public sealed class ToolState
{
    /// <summary>0 = fresh edge, 1 = worn out.</summary>
    public float Wear { get; private set; }
    public bool Broken { get; private set; }

    /// <summary>How far a worn edge has receded from where it was (mm).</summary>
    public float EdgeLossMm => Wear * 0.15f;

    /// <summary>True when this wear finished the tool off.</summary>
    public bool AddWear(float amount)
    {
        if (Broken)
            return false;
        Wear = System.Math.Min(1f, Wear + amount);
        if (Wear < 1f)
            return false;
        Broken = true;
        return true;
    }

    public void Break() => Broken = true;
}

/// <summary>Every tool of one machine and the state it is in. Engine-free.</summary>
public sealed class ToolCrib
{
    private readonly Dictionary<int, ToolState> _tools = new();

    public ToolState this[int toolNumber]
    {
        get
        {
            if (!_tools.TryGetValue(toolNumber, out var state))
                _tools[toolNumber] = state = new ToolState();
            return state;
        }
    }

    public bool IsBroken(int toolNumber) => _tools.TryGetValue(toolNumber, out var state) && state.Broken;

    /// <summary>A fresh tool has been put in this pocket.</summary>
    public void Replace(int toolNumber) => _tools[toolNumber] = new ToolState();
}
