using System;
using System.Collections.Generic;
using Godot;
using CncTrainer.Fixtures;
using CncTrainer.Interaction;
using CncTrainer.Machining;
using CncTrainer.Workshop;
using GDict = Godot.Collections.Dictionary;

namespace CncTrainer.Save;

/// <summary>A carryable that can be written to a save and made again from it.</summary>
public interface ISavedItem
{
    GDict SaveState();
}

/// <summary>Something that saves the items it holds as part of its own
/// state (a chuck's bar, a vise's stock, the player's belt): everything
/// below it in the tree, and HeldItems kept elsewhere, is not saved again
/// as a loose item.</summary>
public interface IItemHolder
{
    IEnumerable<Node> HeldItems => Array.Empty<Node>();
}

/// <summary>A node with state of its own in a save, found anywhere in the
/// workshop and keyed by its path. Load runs on a freshly opened workshop.</summary>
public interface ISaveable
{
    GDict Save();
    void Load(GDict data);
}

/// <summary>Item records: what kind of thing, its own state, its name.</summary>
public static class SavedItems
{
    private static readonly Dictionary<string, Func<GDict, Carryable>> Factories = new()
    {
        [nameof(StockItem)] = StockItem.Restore,
        [nameof(ToolItem)] = ToolItem.Restore,
        [nameof(HandTool)] = HandTool.Restore,
        [nameof(JawItem)] = JawItem.Restore,
        [nameof(ColletItem)] = ColletItem.Restore,
        [nameof(ChuckItem)] = ChuckItem.Restore,
        [nameof(ChuckStand)] = ChuckStand.Restore,
        [nameof(BoltItem)] = _ => BoltItem.Create(),
        [nameof(ClampItem)] = _ => ClampItem.Create(),
        [nameof(Vise)] = _ => Vise.Create(),
        [nameof(CentringPlate)] = _ => CentringPlate.Create(),
    };

    public static GDict Record(Node item) => item is ISavedItem saved and Carryable carryable
        ? new GDict { ["kind"] = item.GetType().Name, ["state"] = saved.SaveState(), ["name"] = carryable.DisplayName }
        : null;

    /// <summary>The item made again, not yet in the tree; null for an unknown kind.</summary>
    public static Carryable Make(GDict record)
    {
        if (record == null || !Factories.TryGetValue(record["kind"].AsString(), out var factory))
            return null;
        var item = factory(record["state"].AsGodotDictionary());
        if (item != null)
            item.DisplayName = record["name"].AsString();
        return item;
    }

    /// <summary>Makes it again into the scene where it was (global frame).</summary>
    public static Carryable Place(GDict record, Node scene)
    {
        var item = Make(record);
        if (item == null)
            return null;
        item.Freeze = record["frozen"].AsBool();
        scene.AddChild(item);
        item.GlobalTransform = record["at"].AsTransform3D();
        return item;
    }

    /// <summary>A loose item where it lies.</summary>
    public static GDict Loose(Carryable item)
    {
        var record = Record(item);
        if (record != null)
        {
            record["frozen"] = item.Freeze;
            record["at"] = item.GlobalTransform;
        }
        return record;
    }
}
