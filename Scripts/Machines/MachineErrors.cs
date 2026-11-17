using Godot;
using CncTrainer.Bridge;
using CncTrainer.Spec;

namespace CncTrainer;

/// <summary>
/// Where the tool really is when the control says it is somewhere: play in
/// each drive (lost on every reversal, minus what the control compensates)
/// and the spindle growing as it warms up, which carries the tool deeper in Z.
/// </summary>
public sealed class MachineErrors
{
    private readonly MachineSpec _spec;
    private readonly float[] _play = new float[6];
    private readonly float[] _lag = new float[6];
    private readonly float[] _tool = new float[6];
    private bool _started;
    private float _warmth;

    public MachineErrors(MachineSpec spec)
    {
        _spec = spec;
        foreach (var axis in spec.Axes)
            if (axis != null)
                _play[(int)axis.Letter] = axis.UncompensatedPlayMm;
    }

    public float ThermalGrowthMm => _spec.ThermalGrowthMm * _warmth;

    /// <summary>Called with every confirmed sample.</summary>
    public void Update(MachineState state, float seconds)
    {
        for (int i = 0; i < 6; i++)
        {
            if (_play[i] <= 0f)
                continue;
            // The drive pushes the tool only once the play is taken up: the
            // tool trails the command by up to half the play either way.
            float command = state.Position.Get((AxisId)i);
            float half = _play[i] / 2f;
            float tool = _started ? Mathf.Clamp(_tool[i], command - half, command + half) : command;
            _tool[i] = tool;
            _lag[i] = tool - command;
        }
        _started = true;
        float tau = _spec.ThermalWarmUpMinutes * 60f;
        float target = System.Math.Abs(state.SpindleRpm) > 1.0 ? 1f : 0f;
        _warmth += (target - _warmth) * (1f - Mathf.Exp(-seconds / Mathf.Max(tau, 1f)));
    }

    public AxisVector Apply(AxisVector command) => new(
        command.X + _lag[0], command.Y + _lag[1], command.Z + _lag[2] - ThermalGrowthMm,
        command.A + _lag[3], command.B + _lag[4], command.C + _lag[5]);
}
