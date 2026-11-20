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
    private const float RapidVolumeMm3 = 5f;
    private const float IdleBelowMm3PerSec = 0.5f;
    private const double WarnEverySeconds = 6.0;
    private const double DryWarnEverySeconds = 40.0;
    /// <summary>Wear with coolant running, relative to cutting dry.</summary>
    public const float CoolantWearFactor = 0.4f;

    private readonly MachineSpec _spec;
    private readonly CuttingProcess _cutting;
    private readonly MachineSafety _safety;

    private CutSample _last;
    private float _pendingVolume, _removalRate, _surfaceSpeed;
    private float _overloadSeconds, _rubbingSeconds, _stoppedVolume, _rapidVolume;
    private double _sinceCut = 999.0, _sinceWarning = 999.0, _sinceDryWarning = 999.0;

    /// <summary>The verdict on the cutting going on now; empty when idle.</summary>
    public CutLoad Load { get; private set; }
    /// <summary>Chips that cannot get out of a deep pocket or slot.</summary>
    public ChipEvacuation Chips { get; } = new();
    /// <summary>1 = dry cutting; set lower while coolant runs.</summary>
    public float WearFactor = 1f;
    /// <summary>Speeds wear up beyond the real tool life (1 = real).</summary>
    public float WearScale = 1f;
    /// <summary>Worst tool load and the roughest surface since the last
    /// ResetQuality: what the finished part shows.</summary>
    public float PeakToolLoad { get; private set; }
    /// <summary>Ra is the feed marks of the last tool that cut (a finishing
    /// pass wipes out the roughing marks; a parting blade only makes the
    /// end face); chatter marks stay.</summary>
    public SurfaceFinish Surface => _chatter ? new SurfaceFinish(false, _raUm, _springMm).WithChatter() : new SurfaceFinish(false, _raUm, _springMm);
    /// <summary>How far the cutting edge is pushed off the work right now (mm).</summary>
    public float DeflectionMm { get; private set; }
    private float _springMm;
    public bool ChatterMarks => _chatter;
    private bool _chatter;
    private float _raUm;
    private int _raTool = -1;

    public CutMonitor(MachineSpec spec, CuttingProcess cutting, MachineSafety safety)
    {
        _spec = spec;
        _cutting = cutting;
        _safety = safety;
        cutting.CutObserved += OnCut;
    }

    public void ResetQuality()
    {
        PeakToolLoad = 0f;
        _chatter = false;
        _raUm = 0f;
        _springMm = 0f;
        _raTool = -1;
    }

    private void OnCut(CutSample sample)
    {
        _last = sample;
        _sinceCut = 0.0;
        _pendingVolume += sample.VolumeMm3;

        // A rapid move is not a cut: whatever it meets, it hits.
        _rapidVolume = sample.Rapid ? _rapidVolume + sample.VolumeMm3 : 0f;
        if (_rapidVolume > RapidVolumeMm3)
        {
            _rapidVolume = 0f;
            _cutting.Tools.Active(sample.ToolNumber).State?.Break();
            _safety.Trip(IncidentKind.Crash, "ÇARPMA: hızlı hareketle (G0) malzemeye girildi, takım kırıldı");
            return;
        }

        if (Mathf.Abs(sample.SpindleRpm) >= 1f)
        {
            _stoppedVolume = 0f;
            return;
        }
        // Brushing the work with a stopped tool does no harm; pushing into it does.
        _stoppedVolume += sample.VolumeMm3;
        var mounted = _cutting.Tools.Active(sample.ToolNumber);
        if (_stoppedVolume > StoppedSpindleVolumeMm3 && mounted.Present)
            Break(mounted.State, $"TAKIM KIRILDI: {Name(mounted.Spec)} iş mili dönmeden malzemeye sürüldü");
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
        _sinceDryWarning += delta;
        _sinceCut += delta;
        float dt = (float)delta;
        if (dt <= 0f)
            return;
        float blend = Mathf.Clamp(dt / AveragingSeconds, 0f, 1f);
        _removalRate = Mathf.Lerp(_removalRate, _pendingVolume / dt, blend);
        _pendingVolume = 0f;

        var mounted = _cutting.Tools.Active(_last.ToolNumber);
        var tool = mounted.Spec;
        if (_removalRate < IdleBelowMm3PerSec || tool == null || Mathf.Abs(_last.SpindleRpm) < 1f)
        {
            _surfaceSpeed = 0f;
            _overloadSeconds = 0f;
            Load = default;
            DeflectionMm = 0f;
            Chips.Clear(dt, WearFactor < 1f);
            return;
        }
        // The force is power over cutting speed. The averaged power lags the
        // cut, so the speed must not run ahead of it: when a facing cut nears
        // the centre the speed collapses while the average still remembers
        // the larger radius. The larger of the two speeds never overstates the force.
        _surfaceSpeed = Mathf.Lerp(_surfaceSpeed, _last.SurfaceSpeedMmPerSec, blend);
        float speed = Mathf.Max(_surfaceSpeed, _last.SurfaceSpeedMmPerSec);

        var load = CuttingLoad.Evaluate(_removalRate, _last.SpindleRpm, speed, _last.FeedPerRevMm,
            mounted.Limits(), MaterialPreset.Get(_cutting.Material), _spec.SpindlePowerKw * 1000f, WearFactor * WearScale, _cutting.WorkSlenderness());
        Load = load;
        DeflectionMm = mounted.Limits().DeflectionMm(load.ForceN);
        PeakToolLoad = Mathf.Max(PeakToolLoad, load.ToolLoad);
        _chatter |= load.Chatter;
        if (tool.Kind != ToolKind.Parting)
        {
            if (tool.Number != _raTool)
            {
                _raTool = tool.Number;
                _raUm = 0f;
                _springMm = 0f;
            }
            _springMm = Mathf.Max(_springMm, DeflectionMm);
            _raUm = Mathf.Max(_raUm, SurfaceFinish.RaUmFor(_last.FeedPerRevMm,
                tool.IsLatheTool ? tool.TipRadiusMm : tool.DiameterMm / 2f, tool.Flutes, tool.IsLatheTool));
        }
        var state = mounted.State;

        if (load.Fault == CutFault.ToolOverload)
        {
            Break(state, System.FormattableString.Invariant(
                $"TAKIM KIRILDI: {Name(tool)} kesme kuvvetine dayanamadı ({load.ForceN:0} N). Paso ya da ilerleme çok yüksek"));
            return;
        }
        var material = MaterialPreset.Get(_cutting.Material);
        switch (Chips.Track(dt, _removalRate, tool.IsLatheTool ? 0f : _last.DepthMm / Mathf.Max(tool.DiameterMm, 0.1f), material, WearFactor < 1f))
        {
            case ChipEvacuation.Verdict.Packed:
                Break(state, $"TALAŞ SIKIŞTI: {Name(tool)} kanallarına {material.Name} talaşı sıvandı ve kırıldı. Derin cepte soğutma aç (M8), pasoyu böl");
                return;
            case ChipEvacuation.Verdict.Building when _sinceWarning > WarnEverySeconds:
                _sinceWarning = 0.0;
                _safety.Warn("Talaş cepten çıkamıyor, takımın etrafında birikiyor: soğutma aç ya da pasoyu böl");
                break;
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

        if (WearFactor >= 1f && MaterialPreset.Get(_cutting.Material).NeedsCoolant && _sinceCut < 0.1 && _sinceDryWarning > DryWarnEverySeconds)
        {
            _sinceDryWarning = 0.0;
            _safety.Warn($"{MaterialPreset.Get(_cutting.Material).Name} kuru kesiliyor: soğutma sıvısını aç (M8), takım daha yavaş körelir");
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
        var mounted = _cutting.Tools.Active(toolNumber);
        string tool = !mounted.Present ? "takım yok"
            : mounted.State.Broken ? $"T{mounted.Spec.Number} KIRIK" : $"T{mounted.Spec.Number} aşınma %{mounted.State.Wear * 100f:0}";
        return $"YÜK %{Load.SpindleLoad * 100f:0}   {tool}";
    }
}
