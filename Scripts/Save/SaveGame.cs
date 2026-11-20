using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Spec;
using GDict = Godot.Collections.Dictionary;

namespace CncTrainer.Save;

/// <summary>One save file as the menus list it.</summary>
public sealed record SaveInfo(string Path, string Name, DateTime Time, string Scenario);

/// <summary>
/// Saves the whole workshop and puts it back: every ISaveable node by its
/// path (machines and their setups, the player, the job board), and every
/// item lying loose. Items a holder keeps (a chuck's bar, the belt) are in
/// that holder's own state. A save is loaded into a freshly opened workshop.
/// </summary>
public static class SaveGame
{
    public const string Folder = "user://saves/";
    public const string AutoFile = Folder + "auto.sav";
    public const string AutoName = "Otomatik kayıt";
    private const int Version = 1;

    private static readonly Dictionary<string, ControlMemory> PendingControls = new();
    private static GDict _pending;

    public static IReadOnlyList<SaveInfo> List()
    {
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        return (DirAccess.GetFilesAt(Folder) ?? Array.Empty<string>())
            .Where(f => f.EndsWith(".sav"))
            .Select(f => ReadInfo(Folder + f))
            .Where(i => i != null)
            .OrderByDescending(i => i.Time)
            .ToList();
    }

    public static SaveInfo Latest() => List().FirstOrDefault();

    public static void Delete(string path) => DirAccess.RemoveAbsolute(path);

    /// <summary>A new file for a named save.</summary>
    public static string NewPath() => Folder + $"save_{DateTime.Now:yyyyMMdd_HHmmss}.sav";

    private static SaveInfo ReadInfo(string path)
    {
        using var file = FileAccess.OpenCompressed(path, FileAccess.ModeFlags.Read, FileAccess.CompressionMode.Zstd);
        if (file == null || file.GetVar().Obj is not GDict meta || meta["version"].AsInt32() != Version)
            return null;
        return new SaveInfo(path, meta["name"].AsString(), DateTimeOffset.FromUnixTimeSeconds(meta["time"].AsInt64()).LocalDateTime, meta["scenario"].AsString());
    }

    public static bool Write(Node scene, string path, string name, string scenario)
    {
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        var nodes = new GDict();
        foreach (var node in Walk(scene).OfType<ISaveable>())
            nodes[scene.GetPathTo((Node)node).ToString()] = node.Save();
        var data = new GDict
        {
            ["nodes"] = nodes,
            ["loose"] = new Godot.Collections.Array(LooseItems(scene).Select(SavedItems.Loose).Where(r => r != null).Select(r => (Variant)r)),
        };
        var meta = new GDict { ["version"] = Version, ["name"] = name, ["time"] = DateTimeOffset.Now.ToUnixTimeSeconds(), ["scenario"] = scenario };
        using var file = FileAccess.OpenCompressed(path, FileAccess.ModeFlags.Write, FileAccess.CompressionMode.Zstd);
        if (file == null)
        {
            GD.PrintErr($"[SaveGame] cannot write {path}: {FileAccess.GetOpenError()}");
            return false;
        }
        file.StoreVar(meta);
        file.StoreVar(data);
        return true;
    }

    /// <summary>Reads a save ahead of opening the workshop: the machines'
    /// controls start from its offsets, the rest goes in once the workshop is up.</summary>
    public static bool Prepare(string path)
    {
        PendingControls.Clear();
        _pending = null;
        using var file = FileAccess.OpenCompressed(path, FileAccess.ModeFlags.Read, FileAccess.CompressionMode.Zstd);
        if (file == null || file.GetVar().Obj is not GDict meta || meta["version"].AsInt32() != Version || file.GetVar().Obj is not GDict data)
            return false;
        _pending = data;
        foreach (var (_, value) in data["nodes"].AsGodotDictionary())
            if (value.AsGodotDictionary() is { } record && record.TryGetValue("control", out var control))
                PendingControls[record["machine"].AsString()] = SaveCodec.Control(control.AsGodotDictionary());
        return true;
    }

    /// <summary>The offsets a machine's control starts with (once); null = none saved.</summary>
    public static ControlMemory TakeControl(string machineName) =>
        PendingControls.Remove(machineName, out var memory) ? memory : null;

    /// <summary>Puts the prepared save into the workshop that has just opened.</summary>
    public static void ApplyPrepared(Node scene)
    {
        if (_pending is not GDict data)
            return;
        _pending = null;
        foreach (var item in LooseItems(scene))
            item.QueueFree();
        foreach (var (path, value) in data["nodes"].AsGodotDictionary())
            if (scene.GetNodeOrNull(path.AsString()) is ISaveable saveable)
                saveable.Load(value.AsGodotDictionary());
        foreach (var record in data["loose"].AsGodotArray())
            SavedItems.Place(record.AsGodotDictionary(), scene);
    }

    /// <summary>Saved items not kept by any holder.</summary>
    private static List<Carryable> LooseItems(Node scene)
    {
        var held = new HashSet<Node>();
        foreach (var holder in Walk(scene).OfType<IItemHolder>())
            held.UnionWith(holder.HeldItems);
        var loose = new List<Carryable>();
        Collect(scene, false);
        return loose;

        void Collect(Node node, bool underHolder)
        {
            if (!underHolder && node is Carryable item and ISavedItem && !held.Contains(node))
                loose.Add(item);
            bool below = underHolder || node is IItemHolder;
            foreach (var child in node.GetChildren())
                Collect(child, below);
        }
    }

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
            foreach (var descendant in Walk(child))
                yield return descendant;
    }
}
