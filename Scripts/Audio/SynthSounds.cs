using System;
using Godot;

namespace CncTrainer.Audio;

/// <summary>Every sound in the workshop, synthesised once at start (the
/// project ships no audio files). Loops are whole seconds of whole-hertz
/// tones, so they repeat without a click.</summary>
public static class SynthSounds
{
    private const int Rate = 22050;

    private static AudioStreamWav _click, _beep, _motor, _cut, _servo, _coolant;

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
