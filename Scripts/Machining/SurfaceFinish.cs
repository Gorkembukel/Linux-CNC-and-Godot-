using System;

namespace CncTrainer.Machining;

/// <summary>
/// The surface a cut leaves. Ra from the feed marks: a turning insert of nose
/// radius r at feed f leaves a peak-to-valley of f^2 / (8 r), Ra about a
/// quarter of that; a milling cutter of radius R leaves the same kind of
/// cusps with the feed per tooth. Chatter marks are far rougher than any
/// feed mark. Engine-free.
/// </summary>
public readonly record struct SurfaceFinish(bool Chatter, float RaUm)
{
    /// <summary>Ra a chatter-marked surface reads at the least.</summary>
    public const float ChatterRaUm = 6.3f;

    public static SurfaceFinish Clean => new(false, 0f);

    /// <param name="radiusMm">Lathe: the insert's nose radius. Mill: the cutter's radius.</param>
    public static float RaUmFor(float feedPerRevMm, float radiusMm, int flutes, bool lathe)
    {
        float feed = lathe ? feedPerRevMm : feedPerRevMm / Math.Max(1, flutes);
        return feed * feed / (32f * Math.Max(radiusMm, 0.05f)) * 1000f;
    }

    /// <summary>The finish as a cut with chatter would leave it.</summary>
    public SurfaceFinish WithChatter() => new(true, Math.Max(RaUm * 2f, ChatterRaUm));

    /// <summary>Suffix for the finished part's name.</summary>
    public string Note => RaUm <= 0f ? "" : Chatter
        ? FormattableString.Invariant($" (titreşim izli, Ra {RaUm:0.0})")
        : FormattableString.Invariant($" (Ra {RaUm:0.0})");
}
