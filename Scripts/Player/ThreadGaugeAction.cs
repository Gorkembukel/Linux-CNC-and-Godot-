using Godot;
using CncTrainer.Interaction;
using CncTrainer.Material;

namespace CncTrainer.Player;

/// <summary>
/// The M20x2 ring gauge in hand: a click puts it on the free end of the
/// thread looked at, the wheel screws it on (30 deg a notch) and back off,
/// right click turns it round (GO / NO-GO side) while it is off. The GO side
/// must run the whole thread; the NO-GO side must stop within a turn and a
/// half. While it is on, it sits on the part.
/// </summary>
public partial class ThreadGaugeAction : Node
{
    public PlayerController Player;

    private const float TurnPerNotch = 1f / 12f, NoGoStopTurns = 1.5f, RefusedTurns = 0.25f;

    private IThreaded _target;
    private ThreadSite? _looked;
    private ThreadSite _site;
    private bool _engaged, _go = true, _reported;
    private float _turns, _stopTurns, _lengthTurns, _pitchMm;
    private string _verdict;

    public string Status { get; private set; }
    public Transform3D? HeldTransform { get; private set; }

    private bool HoldingRing => Player.HeldTool?.Kind == HandToolKind.ThreadRing;
    private string Side => _go ? "GEÇER" : "GEÇMEZ";

    public void Tick(Node3D lookedAt, Vector3 lookPoint)
    {
        Status = null;
        if (!HoldingRing)
        {
            _engaged = false;
            HeldTransform = null;
            return;
        }
        if (_engaged)
        {
            var site = _site;
            var at = site.PointAt(site.FreeEnd + site.Inward * _turns * _pitchMm / site.StepMm);
            var along = site.Axis * site.Inward;
            HeldTransform = new Transform3D(new Basis(along, along.Cross(Vector3.Up).Normalized().Cross(along), along.Cross(Vector3.Up).Normalized()), at);
            Status = System.FormattableString.Invariant(
                $"{Side} tarafı takılı: {_turns:0.0} tur / dişin boyu {_lengthTurns:0.0} tur   [Teker] vidala / sök") + (_verdict != null ? "   " + _verdict : "");
            return;
        }
        HeldTransform = null;
        _target = IThreaded.Find(lookedAt);
        string why = null;
        _looked = _target?.ThreadAt(lookPoint, out why);
        if (_looked != null)
            Status = $"[Tık] Mastarı dişin ucuna tak ({Side} tarafı)   [Sağ tık] mastarı çevir";
        else if (why != null)
            Status = why;
    }

    public bool HandleInput(InputEvent @event)
    {
        if (!HoldingRing)
            return false;
        if (!_engaged)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && _looked != null)
            {
                _go = !_go;
                return true;
            }
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _looked is { } site)
            {
                Engage(site);
                return true;
            }
            return false;
        }
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Screw(TurnPerNotch);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Screw(-TurnPerNotch);
                return true;
            case InputEventMouseButton { Pressed: true }:
                PlayerController.Notify(this, "Mastarı çıkarmak için sök (teker geri)");
                return true;
        }
        return false;
    }

    private void Engage(ThreadSite site)
    {
        // Measured a few millimetres in from the end, where the thread is full.
        int probe = Mathf.Clamp(site.FreeEnd + site.Inward * Mathf.RoundToInt(6f / site.StepMm), 0, site.RadiiMm.Length - 1);
        var form = ThreadCheck.Measure(site.RadiiMm, site.StepMm, probe);
        if (form is not { } f)
        {
            PlayerController.Notify(this, "Bu uçta diş yok");
            return;
        }
        var spec = ThreadSpec.M20x2;
        _site = site;
        _pitchMm = spec.PitchMm;
        _lengthTurns = ThreadCheck.Length(site.RadiiMm, site.StepMm, site.FreeEnd, site.Inward, f.PitchMm) * site.StepMm / spec.PitchMm;
        string refusal = ThreadCheck.GoRefusal(f, spec);
        if (_go)
        {
            _stopTurns = refusal == null ? _lengthTurns : RefusedTurns;
            _verdict = null;
        }
        else
            _stopTurns = ThreadCheck.NoGoEnters(f, spec) ? _lengthTurns : refusal == null ? NoGoStopTurns : RefusedTurns;
        _form = f;
        _refusal = refusal;
        _turns = 0f;
        _reported = false;
        _verdict = null;
        _engaged = true;
    }

    private ThreadForm _form;
    private string _refusal;

    private void Screw(float turns)
    {
        _turns = Mathf.Clamp(_turns + turns, 0f, _stopTurns);
        if (_turns <= 0f && turns < 0f)
        {
            _engaged = false;
            HeldTransform = null;
            return;
        }
        if (_turns < _stopTurns - 1e-4f || _reported)
            return;
        _reported = true;
        bool full = _stopTurns >= _lengthTurns - 1e-4f;
        _verdict = (_go, full) switch
        {
            (true, true) => "GEÇER tarafı sonuna kadar girdi ✓",
            (true, false) => $"GEÇER tarafı girmiyor ✗ {_refusal}",
            (false, true) => System.FormattableString.Invariant($"GEÇMEZ tarafı giriyor ✗ diş fazla derin (bölüm çapı {_form.PitchDiameterMm:0.000})"),
            (false, false) => _refusal == null ? "GEÇMEZ tarafı 1,5 turda durdu ✓ diş ölçüde" : $"GEÇMEZ tarafı da girmiyor: {_refusal}",
        };
        PlayerController.Notify(this, _verdict);
    }
}
