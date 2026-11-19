using Godot;

namespace CncTrainer.Fixtures;

/// <summary>Spindle nose standard (ISO 702-2 camlock): a chuck fits only a
/// spindle with the same nose.</summary>
public enum SpindleNose
{
    D1_3,
    D1_4,
    D1_5,
    D1_6,
    D1_8,
}

public static class SpindleNoses
{
    public static string NameOf(SpindleNose nose) => nose.ToString().Replace('_', '-');

    /// <summary>Diameter of the nose's register (mm, ISO 702-2).</summary>
    public static float DiameterMm(SpindleNose nose) => nose switch
    {
        SpindleNose.D1_3 => 92f,
        SpindleNose.D1_4 => 117.5f,
        SpindleNose.D1_5 => 146f,
        SpindleNose.D1_6 => 171.4f,
        _ => 225.4f,
    };

    /// <summary>Cam locks on a nose of this size (D1-6 and up have six).</summary>
    public static int CamCount(SpindleNose nose) => nose >= SpindleNose.D1_6 ? 6 : 3;
}

/// <summary>A chuck as a thing of its own: its sizes do not depend on the
/// lathe it is put on. A lathe takes it when it clears the bed.</summary>
[Tool, GlobalClass]
public partial class ChuckModel : Resource
{
    /// <summary>Name on the chuck, in the UI and on the setup sheet.</summary>
    [Export] public string DisplayName = "3 ayaklı ayna";
    /// <summary>Scroll (self-centring), independent four-jaw, or collet.</summary>
    [Export] public ChuckKind Kind = ChuckKind.ThreeJaw;
    /// <summary>The spindle nose its back plate is made for: it goes only
    /// on a lathe with the same nose.</summary>
    [Export] public SpindleNose Mount = SpindleNose.D1_4;
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
        new(Kind, JawCount, MaxOpeningMm, JawDepthMm, BoreMm, boreDepthMm, ScrollPitchMm, JawFamily);

    /// <summary>Which jaws fit it: jaws are made for one kind and size of chuck.</summary>
    public string JawFamily => System.FormattableString.Invariant($"{Kind}-{DiameterMm:0}");

    /// <summary>Cam locks holding it to the nose.</summary>
    public int CamCount => SpindleNoses.CamCount(Mount);

    /// <summary>Length of the body behind the jaws.</summary>
    public float BodyLengthMm => Mathf.Max(10f, LengthMm - JawDepthMm * 0.7f);

    /// <summary>A three-jaw chuck sized from the older per-lathe settings
    /// (the chuck a lathe comes with).</summary>
    public static ChuckModel ThreeJaw(float diameterMm, int jawCount, float jawDepthMm, float maxRpm, SpindleNose mount = SpindleNose.D1_4) => new()
    {
        DisplayName = System.FormattableString.Invariant($"{jawCount} ayaklı ayna Ø{diameterMm:0}"),
        Mount = mount,
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
        Mount = SpindleNose.D1_4,
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
        Mount = SpindleNose.D1_4,
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

    /// <summary>A chuck cabinet's worth: scroll chucks in the usual sizes on
    /// the three common noses, four-jaw independents and collet chucks.</summary>
    public static ChuckModel[] Catalogue() => new[]
    {
        Scaled(ChuckKind.ThreeJaw, 80f, SpindleNose.D1_3, 5000f),
        Scaled(ChuckKind.ThreeJaw, 100f, SpindleNose.D1_4, 4500f),
        Scaled(ChuckKind.ThreeJaw, 125f, SpindleNose.D1_4, 4000f),
        Scaled(ChuckKind.ThreeJaw, 160f, SpindleNose.D1_5, 3500f),
        Scaled(ChuckKind.ThreeJaw, 200f, SpindleNose.D1_6, 3000f),
        Scaled(ChuckKind.ThreeJaw, 250f, SpindleNose.D1_6, 2500f),
        FourJaw125(),
        Scaled(ChuckKind.FourJawIndependent, 200f, SpindleNose.D1_6, 1500f),
        ColletEr32(),
        new ChuckModel
        {
            DisplayName = "ER40 pens aynası", Kind = ChuckKind.Collet, Mount = SpindleNose.D1_6, DiameterMm = 120f, LengthMm = 85f,
            JawCount = 0, JawDepthMm = 35f, MaxOpeningMm = 26f, BoreMm = 26f, MaxRpm = 5000f,
            ColletSizesMm = new[] { 10f, 14f, 18f, 22f, 26f }, MassKg = 9f,
        },
    };

    /// <summary>A jaw chuck with proportions typical of its size.</summary>
    public static ChuckModel Scaled(ChuckKind kind, float diameterMm, SpindleNose mount, float maxRpm)
    {
        bool four = kind == ChuckKind.FourJawIndependent;
        return new ChuckModel
        {
            DisplayName = System.FormattableString.Invariant($"{(four ? "4 ayaklı bağımsız" : "3 ayaklı")} ayna Ø{diameterMm:0}"),
            Kind = kind, Mount = mount, DiameterMm = diameterMm,
            LengthMm = 45f + diameterMm * 0.3f, JawCount = four ? 4 : 3,
            JawDepthMm = 15f + diameterMm * 0.1f, MaxOpeningMm = diameterMm * (four ? 0.88f : 0.8f), BoreMm = diameterMm * 0.27f,
            MaxRpm = maxRpm, ScrollPitchMm = 4f + diameterMm * 0.02f,
            MassKg = Mathf.Round(diameterMm * diameterMm * 0.0007f * (four ? 1.2f : 1f) * 2f) / 2f,
        };
    }
}
