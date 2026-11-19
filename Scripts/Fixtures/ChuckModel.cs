using Godot;

namespace CncTrainer.Fixtures;

/// <summary>A chuck as a thing of its own: its sizes do not depend on the
/// lathe it is put on. A lathe takes it when it clears the bed.</summary>
[Tool, GlobalClass]
public partial class ChuckModel : Resource
{
    /// <summary>Name on the chuck, in the UI and on the setup sheet.</summary>
    [Export] public string DisplayName = "3 ayaklı ayna";
    /// <summary>Scroll (self-centring), independent four-jaw, or collet.</summary>
    [Export] public ChuckKind Kind = ChuckKind.ThreeJaw;
    /// <summary>Outside diameter of the body.</summary>
    [Export(PropertyHint.Range, "40,400,1,suffix:mm")] public float DiameterMm = 100f;
    /// <summary>From the spindle nose to the front face of the jaws (or the
    /// collet nose): the work sits this much further out with this chuck.</summary>
    [Export(PropertyHint.Range, "20,300,1,suffix:mm")] public float LengthMm = 80f;
    /// <summary>Number of jaw slots (scroll and independent chucks).</summary>
    [Export(PropertyHint.Range, "2,6,1")] public int JawCount = 3;
    /// <summary>How long a stretch of the bar the jaws (or the collet) hold.</summary>
    [Export(PropertyHint.Range, "5,100,1,suffix:mm")] public float JawDepthMm = 30f;
    /// <summary>Widest the jaws open (diameter they still close on).</summary>
    [Export(PropertyHint.Range, "5,400,1,suffix:mm")] public float MaxOpeningMm = 80f;
    /// <summary>Hole through the chuck: bars thinner than it go in past the jaws.</summary>
    [Export(PropertyHint.Range, "0,200,1,suffix:mm")] public float BoreMm = 30f;
    /// <summary>Highest speed the chuck is rated for. The control is set up
    /// for the chuck the lathe came with; running another one faster is warned.</summary>
    [Export(PropertyHint.Range, "100,12000,10,suffix:rpm")] public float MaxRpm = 4000f;
    /// <summary>Scroll chucks: how far the jaws move per turn of the scroll.</summary>
    [Export(PropertyHint.Range, "1,20,0.5,suffix:mm")] public float ScrollPitchMm = 6f;
    /// <summary>Collet chucks: the collets in its box, by size (each holds
    /// from its size down to 1 mm under).</summary>
    [Export] public float[] ColletSizesMm = System.Array.Empty<float>();
    /// <summary>Weight, for carrying it about.</summary>
    [Export(PropertyHint.Range, "1,100,0.5,suffix:kg")] public float MassKg = 10f;

    /// <summary>Its sizes for the grip model; bars this far into the
    /// spindle bore still go in past the jaws.</summary>
    public ChuckDims Dims(float boreDepthMm) =>
        new(Kind, JawCount, MaxOpeningMm, JawDepthMm, BoreMm, boreDepthMm, ScrollPitchMm);

    /// <summary>Length of the body behind the jaws.</summary>
    public float BodyLengthMm => Mathf.Max(10f, LengthMm - JawDepthMm * 0.7f);

    /// <summary>A three-jaw chuck sized from the older per-lathe settings
    /// (the chuck a lathe comes with).</summary>
    public static ChuckModel ThreeJaw(float diameterMm, int jawCount, float jawDepthMm, float maxRpm) => new()
    {
        DisplayName = jawCount == 3 ? "3 ayaklı ayna" : $"{jawCount} ayaklı ayna",
        Kind = ChuckKind.ThreeJaw,
        DiameterMm = diameterMm,
        LengthMm = 50f + jawDepthMm,
        JawCount = jawCount,
        JawDepthMm = jawDepthMm,
        MaxOpeningMm = diameterMm * 0.8f,
        BoreMm = diameterMm * 0.3f,
        MaxRpm = maxRpm,
        MassKg = diameterMm / 10f,
    };

    /// <summary>A Ø125 four-jaw independent chuck.</summary>
    public static ChuckModel FourJaw125() => new()
    {
        DisplayName = "4 ayaklı bağımsız ayna Ø125",
        Kind = ChuckKind.FourJawIndependent,
        DiameterMm = 125f,
        LengthMm = 85f,
        JawCount = 4,
        JawDepthMm = 30f,
        MaxOpeningMm = 110f,
        BoreMm = 32f,
        MaxRpm = 2000f,
        MassKg = 11f,
    };

    /// <summary>An ER32 collet chuck with a box of collets.</summary>
    public static ChuckModel ColletEr32() => new()
    {
        DisplayName = "ER32 pens aynası",
        Kind = ChuckKind.Collet,
        DiameterMm = 90f,
        LengthMm = 75f,
        JawCount = 0,
        JawDepthMm = 30f,
        MaxOpeningMm = 20f,
        BoreMm = 20f,
        MaxRpm = 6000f,
        ColletSizesMm = new[] { 6f, 8f, 10f, 12f, 16f, 20f },
        MassKg = 6f,
    };
}
