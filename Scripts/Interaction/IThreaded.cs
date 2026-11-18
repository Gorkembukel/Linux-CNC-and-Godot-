using Godot;

namespace CncTrainer.Interaction;

/// <summary>A turned outside a thread gauge can be put on: its radii every
/// StepMm from Start (global) along Axis (unit), the bucket looked at, and
/// the free end a ring goes on from.</summary>
public readonly record struct ThreadSite(float[] RadiiMm, float StepMm, int Index, Vector3 Start, Vector3 Axis, int FreeEnd)
{
    /// <summary>From the free end toward the rest of the part.</summary>
    public int Inward => FreeEnd == 0 ? 1 : -1;

    public Vector3 PointAt(float bucket) => Start + Axis * (bucket * StepMm * WorldScale.MmToM);
}

/// <summary>Something turned whose thread can be gauged.</summary>
public interface IThreaded
{
    /// <summary>The part at the point looked at, or null with why not.</summary>
    ThreadSite? ThreadAt(Vector3 globalPoint, out string why);

    public static IThreaded Find(Node node)
    {
        for (; node != null; node = node.GetParent())
            if (node is IThreaded threaded)
                return threaded;
        return null;
    }
}
