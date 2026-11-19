using System;
using Godot;

namespace CncTrainer.Audio;

/// <summary>Every sound in the workshop, synthesised once at start (the
/// project ships no audio files). Loops are whole seconds of whole-hertz
/// tones, so they repeat without a click.</summary>
public static class SynthSounds
{
    private const int Rate = 22050;

    private static AudioStreamWav _click, _beep, _motor, _cut, _servo, _coolant, _chatter;
    private static AudioStreamWav _clink, _clunk, _ratchet, _clack, _scrape;

    /// <summary>A small steel part knocking on steel: high, inharmonic, short.</summary>
    public static AudioStreamWav Clink => _clink ??= Make(Generate(0.25f, (t, rnd) =>
        (Mathf.Sin(Tau * 2930f * t) * 0.4f + Mathf.Sin(Tau * 4170f * t) * 0.3f + Mathf.Sin(Tau * 6310f * t) * 0.2f)
        * Mathf.Exp(-t * 28f) + Noise(rnd) * 0.3f * Mathf.Exp(-t * 400f)), false);

    /// <summary>A jaw dropping into its groove: a lower, duller knock.</summary>
    public static AudioStreamWav Clunk => _clunk ??= Make(Generate(0.3f, (t, rnd) =>
        (Mathf.Sin(Tau * 870f * t) * 0.45f + Mathf.Sin(Tau * 1420f * t) * 0.3f + Mathf.Sin(Tau * 2350f * t) * 0.15f)
        * Mathf.Exp(-t * 22f) + Noise(rnd) * 0.5f * Mathf.Exp(-t * 160f)), false);

    /// <summary>One notch of a chuck key: the scroll's teeth ticking past.</summary>
    public static AudioStreamWav Ratchet => _ratchet ??= Make(Generate(0.05f, (t, rnd) =>
        (Noise(rnd) * 0.5f + Mathf.Sin(Tau * 1850f * t) * 0.4f) * Mathf.Exp(-t * 140f)), false);

    /// <summary>The scroll's thread catching a jaw: a firm metallic clack.</summary>
    public static AudioStreamWav Clack => _clack ??= Make(Generate(0.2f, (t, rnd) =>
        (Mathf.Sin(Tau * 620f * t) * 0.5f + Mathf.Sin(Tau * 1130f * t) * 0.35f + Mathf.Sin(Tau * 3300f * t) * 0.15f)
        * Mathf.Exp(-t * 35f) + Noise(rnd) * 0.6f * Mathf.Exp(-t * 250f)), false);

    /// <summary>A jaw sliding out along its groove.</summary>
    public static AudioStreamWav Scrape => _scrape ??= Make(Filtered(0.35f, 0.5f, 0.08f, 0.6f, t => 0f), false);

    public static AudioStreamWav Click => _click ??= Make(Generate(0.03f, (t, rnd) =>
        (Noise(rnd) * 0.6f + Mathf.Sin(Tau * 2600f * t) * 0.5f) * Mathf.Exp(-t * 220f)), false);

    /// <summary>Three short alarm beeps.</summary>
    public static AudioStreamWav Beep => _beep ??= Make(Generate(0.6f, (t, _) =>
        t % 0.2f < 0.12f ? Mathf.Sin(Tau * 2700f * t) * 0.35f : 0f), false);

    /// <summary>Spindle motor: a 100 Hz fundamental with the harmonics a
    /// motor and belt drive add.</summary>
    public static AudioStreamWav Motor => _motor ??= Make(Generate(1f, (t, rnd) =>
        Mathf.Sin(Tau * 100f * t) * 0.35f + Mathf.Sin(Tau * 200f * t) * 0.25f + Mathf.Sin(Tau * 300f * t) * 0.12f
        + Mathf.Sin(Tau * 600f * t) * 0.08f + Noise(rnd) * 0.04f), true);

    /// <summary>Cutting: band-limited noise with a tooth-pass tone that the
    /// player's pitch follows the spindle speed.</summary>
    public static AudioStreamWav Cut => _cut ??= Make(Filtered(1f, 0.25f, 0.02f, 0.7f, t =>
        Mathf.Sin(Tau * 420f * t) * 0.25f * (0.6f + 0.4f * Mathf.Sin(Tau * 7f * t))), true);

    /// <summary>Chatter: the harsh, pulsing squeal of tool and work shaking
    /// against each other, the sound an operator stops the cut for.</summary>
    public static AudioStreamWav Chatter => _chatter ??= Make(Generate(1f, (t, rnd) =>
        (Mathf.Sin(Tau * 1750f * t) * 0.3f + Mathf.Sin(Tau * 3500f * t) * 0.12f + Noise(rnd) * 0.06f)
        * (0.55f + 0.45f * Mathf.Sin(Tau * 32f * t))), true);

    /// <summary>Axis servo and ballscrew whine.</summary>
    public static AudioStreamWav Servo => _servo ??= Make(Generate(1f, (t, rnd) =>
        Mathf.Sin(Tau * 800f * t) * 0.2f + Mathf.Sin(Tau * 1600f * t) * 0.08f + Noise(rnd) * 0.05f), true);

    /// <summary>Flood coolant hiss.</summary>
    public static AudioStreamWav Coolant => _coolant ??= Make(Filtered(1f, 0.6f, 0.15f, 0.5f, t =>
        0f), true);

    private const float Tau = Mathf.Tau;

    private static float Noise(Random rnd) => (float)(rnd.NextDouble() * 2.0 - 1.0);

    private static float[] Generate(float seconds, Func<float, Random, float> sample)
    {
        var rnd = new Random(7);
        var data = new float[(int)(seconds * Rate)];
        for (int i = 0; i < data.Length; i++)
            data[i] = sample(i / (float)Rate, rnd);
        return data;
    }

    /// <summary>Noise through a one-pole low-pass (`low`) minus a slower
    /// one (`high`), i.e. a rough band-pass, plus an optional tone.</summary>
    private static float[] Filtered(float seconds, float low, float high, float gain, Func<float, float> tone)
    {
        var rnd = new Random(11);
        var data = new float[(int)(seconds * Rate)];
        float a = 0f, b = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float n = Noise(rnd);
            a += (n - a) * low;
            b += (n - b) * high;
            data[i] = (a - b) * gain + tone(i / (float)Rate);
        }
        return data;
    }

    private static AudioStreamWav Make(float[] samples, bool loop)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f);
            bytes[2 * i] = (byte)v;
            bytes[2 * i + 1] = (byte)(v >> 8);
        }
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = bytes,
        };
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = samples.Length;
        }
        return stream;
    }
}
