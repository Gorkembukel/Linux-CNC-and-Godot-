using Godot;

namespace CncTrainer.Bridge;

/// <summary>
/// Generalizes the old lathe-only "Vector2(X=radius, Z stored in the Y
/// field)" convention to however many axes a real machine might have (up to
/// AxisId's 6). Plain value type with 6 explicit named fields (no backing
/// array) -- same zero-allocation shape as Godot's own Vector2/Vector3,
/// deliberately not a `float[]`-backed struct: this is constructed once per
/// incoming STATE packet (100Hz), and a reference-type-backed struct would
/// mean fresh heap garbage every packet for no benefit.
/// </summary>
public readonly struct AxisVector
{
    public readonly float X, Y, Z, A, B, C;

    public static readonly AxisVector Zero = new();

    public AxisVector(float x = 0f, float y = 0f, float z = 0f, float a = 0f, float b = 0f, float c = 0f)
    {
        X = x; Y = y; Z = z; A = a; B = b; C = c;
    }

    public float Get(AxisId axis) => axis switch
    {
        AxisId.X => X,
        AxisId.Y => Y,
        AxisId.Z => Z,
        AxisId.A => A,
        AxisId.B => B,
        AxisId.C => C,
        _ => 0f,
    };

    public static AxisVector operator +(AxisVector a, AxisVector b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.A + b.A, a.B + b.B, a.C + b.C);

    public static AxisVector operator -(AxisVector a, AxisVector b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.A - b.A, a.B - b.B, a.C - b.C);

    public static AxisVector operator *(AxisVector a, float s) =>
        new(a.X * s, a.Y * s, a.Z * s, a.A * s, a.B * s, a.C * s);

    public float Length() => Mathf.Sqrt(X * X + Y * Y + Z * Z + A * A + B * B + C * C);
}
