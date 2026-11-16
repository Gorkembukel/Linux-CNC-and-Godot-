using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Machining;
using CncTrainer.Spec;

namespace CncTrainer;

/// <summary>One confirmed step of cutting: what the tool took off between
/// two machine samples and under which conditions.</summary>
public readonly record struct CutSample(
    int ToolNumber, float VolumeMm3, float Seconds, float SpindleRpm, float FeedMmPerSec,
    float DepthMm, float FeedPerRevMm, float SurfaceSpeedMmPerSec);

/// <summary>
/// What a lathe and a mill have in common while cutting: confirmed machine
/// samples remove material, every cut is reported as a CutSample (chips, and
/// anything else that wants to judge the cut, listen to CutObserved), and a
/// cut on stock that is not held firmly throws it.
/// </summary>
public abstract class CuttingProcess
{
    private const float LooseVolumeMm3 = 20f;

    protected readonly MachineSpec Spec;
    protected readonly BridgeClient Bridge;
    private readonly ChipSpawner _chips;

    private ChipFormation? _chipFormation;
    private double _timeSinceLastCut = 999.0;
    private float _removedWhileLoose;
    private long _lastSampleNs;

    /// <summary>What the stock being cut is made of.</summary>
    public MaterialKind Material;
    /// <summary>Asked on every cut: is the stock held firmly? Null = always.</summary>
    public System.Func<bool> IsSecure;
    /// <summary>Raised once when a real cut is taken on stock that is not secure.</summary>
    public System.Action CutWhileLoose;
    public event System.Action<CutSample> CutObserved;
    /// <summary>What is really cutting when the control says tool N.</summary>
    public IToolSource Tools;

    protected CuttingProcess(MachineSpec spec, BridgeClient bridge, ChipSpawner chips)
    {
        Spec = spec;
        Bridge = bridge;
        _chips = chips;
        Material = spec.StockMaterial;
        Tools = new SpecTools(spec);
    }

    public void OnConfirmedState()
    {
        long now = Bridge.State.LastTMonoNs;
        float seconds = _lastSampleNs == 0 ? 0f : Mathf.Clamp((now - _lastSampleNs) * 1e-9f, 0f, 0.5f);
        _lastSampleNs = now;
        Cut(Bridge.State, seconds);
    }

    /// <summary>Removes what the tool passed through since the last sample.</summary>
    protected abstract void Cut(MachineState state, float seconds);

    /// <summary>Moves the machine body to the predicted position and
    /// returns where the tool meets the work (body frame, metres).</summary>
    protected abstract Vector3 MoveBody(double delta);

    /// <summary>Called by the machine type for every cut it confirms.
    /// False when the cut threw the stock out.</summary>
    protected bool Report(CutSample sample)
    {
        _chipFormation = ChipClassifier.Classify(MaterialPreset.Get(Material), sample.DepthMm, sample.FeedPerRevMm, sample.SurfaceSpeedMmPerSec);
        _timeSinceLastCut = 0.0;
        CutObserved?.Invoke(sample);

        if (IsSecure == null || IsSecure())
            return true;
        // A light touch only rattles the part; a real cut throws it.
        _removedWhileLoose += sample.VolumeMm3;
        if (_removedWhileLoose <= LooseVolumeMm3)
            return true;
        _removedWhileLoose = 0f;
        CutWhileLoose?.Invoke();
        return false;
    }

    /// <summary>Different stock is being cut from now on.</summary>
    protected void ForgetLooseCuts() => _removedWhileLoose = 0f;

    public void Process(double delta)
    {
        var contact = MoveBody(delta);
        _timeSinceLastCut += delta;
        if (_timeSinceLastCut > 0.2)
            _chipFormation = null;
        _chips.Tick(delta, _chipFormation, contact);
    }
}
