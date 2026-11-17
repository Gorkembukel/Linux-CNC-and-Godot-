using Godot;
using CncTrainer.Bridge;

namespace CncTrainer.Audio;

/// <summary>A machine's running sounds, driven by its confirmed state:
/// spindle motor (pitch follows rpm), cutting (loudness follows the cutting
/// load), axis servos (follow the feed speed) and flood coolant.</summary>
public partial class MachineAudio : Node3D
{
    public MachineRig Rig;

    private AudioStreamPlayer3D _motor, _cut, _servo, _coolant;

    public override void _Ready()
    {
        _motor = Player(SynthSounds.Motor);
        _cut = Player(SynthSounds.Cut);
        _servo = Player(SynthSounds.Servo);
        _coolant = Player(SynthSounds.Coolant);
    }

    private AudioStreamPlayer3D Player(AudioStream stream)
    {
        var player = new AudioStreamPlayer3D { Stream = stream, VolumeDb = -80f, UnitSize = 2.5f, MaxDistance = 18f, Autoplay = true };
        AddChild(player);
        return player;
    }

    public override void _Process(double delta)
    {
        if (Rig == null)
            return;
        var s = Rig.Bridge.State;
        bool live = Rig.Bridge.IsBridgeConnected;
        float dt = (float)delta;
        float rpm = Mathf.Abs((float)s.SpindleRpm);

        bool spinning = live && s.SpindleEnabled && rpm > 1f;
        Fade(_motor, spinning ? -10f : -80f, dt);
        _motor.PitchScale = Mathf.Clamp(0.45f + rpm / 4000f, 0.4f, 2.5f);

        float load = live ? Rig.Monitor?.Load.SpindleLoad ?? 0f : 0f;
        Fade(_cut, spinning && load > 0.003f ? Mathf.LinearToDb(Mathf.Clamp(0.25f + load, 0f, 1.2f)) - 4f : -80f, dt * 2f);
        _cut.PitchScale = Mathf.Clamp(0.7f + rpm / 6000f, 0.5f, 2f);

        float speed = live ? (float)s.Velocity.Length() : 0f;
        Fade(_servo, speed > 0.05f ? Mathf.LinearToDb(Mathf.Clamp(speed / 60f, 0.15f, 1f)) - 16f : -80f, dt * 2f);
        _servo.PitchScale = Mathf.Clamp(0.6f + speed / 80f, 0.6f, 2f);

        Fade(_coolant, live && s.Coolant ? -14f : -80f, dt);
    }

    private static void Fade(AudioStreamPlayer3D player, float targetDb, float dt) =>
        player.VolumeDb = Mathf.MoveToward(player.VolumeDb, targetDb, dt * 120f);
}
