using Godot;
using CncTrainer.Chips;
using CncTrainer.Spec;

namespace CncTrainer.Machining;

/// <summary>
/// Judges the cutting a machine does: spindle load for the load meter, tool
/// load and wear for the tool crib, and the faults that stop the machine (a
/// stalled spindle, a snapped tool, a tool pushed into the work with the
/// spindle stopped). Listens to CuttingProcess.CutObserved.
///
/// Material comes off in bursts (a lathe bucket or a voxel layer at a time),
/// so the removal rate is averaged over real time, once per frame, rather
/// than taken from single samples.
/// </summary>
public sealed class CutMonitor
{
    private const float AveragingSeconds = 0.5f;
    private const float StallAfterSeconds = 0.5f;
    private const float StoppedSpindleVolumeMm3 = 15f;
    private const float IdleBelowMm3PerSec = 0.5f;
    private const double WarnEverySeconds = 6.0;

    private readonly MachineSpec _spec;
    private readonly CuttingProcess _cutting;
    private readonly MachineSafety _safety;

    private CutSample _last;
    private float _pendingVolume, _removalRate, _surfaceSpeed;
    private float _overloadSeconds, _rubbingSeconds, _stoppedVolume;
    private double _sinceCut = 999.0, _sinceWarning = 999.0;

    public ToolCrib Tools { get; } = new();
    /// <summary>The verdict on the cutting going on now; empty when idle.</summary>
    public CutLoad Load { get; private set; }
    /// <summary>1 = dry cutting; set lower while coolant runs.</summary>
    public float WearFactor = 1f;
    /// <summary>Worst tool load and whether chatter marked the surface since
    /// the last ResetQuality: what the finished part's surface shows.</summary>
    public float PeakToolLoad { get; private set; }
    public bool ChatterMarks { get; private set; }

    public CutMonitor(MachineSpec spec, CuttingProcess cutting, MachineSafety safety)
    {
        _spec = spec;
        _cutting = cutting;
        _safety = safety;
        cutting.Tools = Tools;
        cutting.CutObserved += OnCut;
    }

    public void ResetQuality()
    {
        PeakToolLoad = 0f;
        ChatterMarks = false;
    }

    private void OnCut(CutSample sample)
    {
        _last = sample;
        _sinceCut = 0.0;
        _pendingVolume += sample.VolumeMm3;

        if (Mathf.Abs(sample.SpindleRpm) >= 1f)
        {
            _stoppedVolume = 0f;
            return;
        }
        // Brushing the work with a stopped tool does no harm; pushing into it does.
        _stoppedVolume += sample.VolumeMm3;
        if (_stoppedVolume > StoppedSpindleVolumeMm3 && _spec.FindTool(sample.ToolNumber) is ToolSpec tool)
            Break(Tools[tool.Number], $"TAKIM KIRILDI: {Name(tool)} iş mili dönmeden malzemeye sürüldü");
    }

    private static string Name(ToolSpec tool) => $"T{tool.Number} ({tool.Description})";

    private void Break(ToolState state, string text)
    {
        if (state.Broken)
            return;
        state.Break();
        _stoppedVolume = 0f;
        _safety.Trip(IncidentKind.ToolBroken, text);
    }

    /// <summary>Once per frame.</summary>
    public void Tick(double delta)
    {
        _sinceWarning += delta;
        _sinceCut += delta;
        float dt = (float)delta;
        if (dt <= 0f)
            return;
        float blend = Mathf.Clamp(dt / AveragingSeconds, 0f, 1f);
        _removalRate = Mathf.Lerp(_removalRate, _pendingVolume / dt, blend);
        _pendingVolume = 0f;

        var tool = _spec.FindTool(_last.ToolNumber);
        if (_removalRate < IdleBelowMm3PerSec || tool == null || Mathf.Abs(_last.SpindleRpm) < 1f)
        {
            _surfaceSpeed = 0f;
            _overloadSeconds = 0f;
            Load = default;
            return;
        }
        // The force is power over cutting speed. The averaged power lags the
        // cut, so the speed must not run ahead of it: when a facing cut nears
        // the centre the speed collapses while the average still remembers
        // the larger radius. The larger of the two speeds never overstates the force.
        _surfaceSpeed = Mathf.Lerp(_surfaceSpeed, _last.SurfaceSpeedMmPerSec, blend);
        float speed = Mathf.Max(_surfaceSpeed, _last.SurfaceSpeedMmPerSec);

        var load = CuttingLoad.Evaluate(_removalRate, _last.SpindleRpm, speed, _last.FeedPerRevMm,
            tool.Limits(), MaterialPreset.Get(_cutting.Material), _spec.SpindlePowerKw * 1000f, WearFactor);
        Load = load;
        PeakToolLoad = Mathf.Max(PeakToolLoad, load.ToolLoad);
        ChatterMarks |= load.Chatter;
        var state = Tools[tool.Number];

        if (load.Fault == CutFault.ToolOverload)
        {
            Break(state, System.FormattableString.Invariant(
                $"TAKIM KIRILDI: {Name(tool)} kesme kuvvetine dayanamadı ({load.ForceN:0} N). Paso ya da ilerleme çok yüksek"));
            return;
        }
        _overloadSeconds = load.Fault == CutFault.SpindleOverload ? _overloadSeconds + dt : 0f;
        if (_overloadSeconds > StallAfterSeconds)
        {
            _overloadSeconds = 0f;
            _safety.Trip(IncidentKind.Overload, System.FormattableString.Invariant(
                $"İŞ MİLİ AŞIRI YÜK: %{load.SpindleLoad * 100f:0} ({load.PowerW / 1000f:0.0} kW). Paso ya da ilerleme çok yüksek"));
            return;
        }

        // The edge only wears while it is in the cut.
        if (_sinceCut < AveragingSeconds && state.AddWear(load.WearPerSecond * dt))
        {
            _safety.Trip(IncidentKind.ToolBroken, $"TAKIM BİTTİ: {Name(tool)} köreldi ve kırıldı. Takımı değiştir");
            return;
        }

        // Feed ramps through "too fine" at the start and end of every move.
        _rubbingSeconds = load.Rubbing && _sinceCut < 0.1 ? _rubbingSeconds + dt : 0f;
        if (_sinceWarning < WarnEverySeconds)
            return;
        string warning = load.SpindleLoad > 1f ? System.FormattableString.Invariant($"İş mili yükü %{load.SpindleLoad * 100f:0}: makine zorlanıyor")
            : load.Chatter ? "TİTREŞİM: takım zorlanıyor, yüzey bozuluyor (paso / ilerlemeyi düşür)"
            : load.SpeedRatio > 1.5f ? System.FormattableString.Invariant($"Kesme hızı önerilenin {load.SpeedRatio:0.0} katı: takım hızla köreliyor (devri düşür)")
            : _rubbingSeconds > 1.5f ? "Talaş çok ince: takım kesmiyor, sürtüyor (ilerlemeyi artır ya da devri düşür)"
            : null;
        if (warning != null)
        {
            _sinceWarning = 0.0;
            _safety.Warn(warning);
        }
    }

    /// <summary>One line for the control's screen.</summary>
    public string StatusLine(int toolNumber)
    {
        var state = Tools[toolNumber];
        string tool = state.Broken ? $"T{toolNumber} KIRIK" : $"T{toolNumber} aşınma %{state.Wear * 100f:0}";
        return $"YÜK %{Load.SpindleLoad * 100f:0}   {tool}";
    }
}
