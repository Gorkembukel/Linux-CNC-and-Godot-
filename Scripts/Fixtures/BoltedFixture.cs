using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;

namespace CncTrainer.Fixtures;

/// <summary>A fixture the player carries to the mill table and bolts down:
/// loose until mounted, then held by its bolts, and free to carry away again
/// once it is empty and every bolt is out.</summary>
public abstract partial class BoltedFixture : Carryable, Save.ISavedItem
{
    /// <summary>Loose, a fixture is just itself; on a table the table saves how it sits.</summary>
    public Godot.Collections.Dictionary SaveState() => new();

    public MillTable Table { get; private set; }
    public readonly List<Fastener> Bolts = new();

    public bool IsMounted => Table != null;
    public bool BoltsTight => Bolts.Count > 0 && Bolts.All(b => b.IsTight);
    public override bool CanPickUp => !IsMounted || (!Loaded && Bolts.All(b => !b.Present));

    /// <summary>Something is still in or on the fixture.</summary>
    protected abstract bool Loaded { get; }

    public override void OnPickedUp()
    {
        if (!IsMounted)
            return;
        foreach (var bolt in Bolts)
            bolt.QueueFree();
        Bolts.Clear();
        Table = null;
        Reparent(GetTree().CurrentScene);
    }

    /// <summary>Sets the fixture on the table and opens its bolt holes
    /// (positions in the fixture's own frame).</summary>
    protected void Mount(MillTable table, IEnumerable<Vector3> boltHoles)
    {
        Table = table;
        Freeze = true;
        Reparent(table);
        foreach (var hole in boltHoles)
        {
            var bolt = new Fastener { Name = "HoldDown", Position = hole };
            AddChild(bolt);
            Bolts.Add(bolt);
        }
    }

    protected int[] SaveBolts() => Bolts.Select(b => b.SavedTurns).ToArray();

    protected void RestoreBolts(int[] turns)
    {
        for (int i = 0; i < Bolts.Count && i < turns.Length; i++)
            Bolts[i].Restore(turns[i]);
    }

    protected string BoltsSheetLine(string label) =>
        Sheet.Mark(BoltsTight) + $"{label}  {Bolts.Count(b => b.IsTight)}/{Bolts.Count}\n";
}
