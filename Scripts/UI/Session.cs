using Godot;
using CncTrainer.Inspection;

namespace CncTrainer.UI;

/// <summary>How the workshop is to start: empty, with a work order open,
/// or from a save file.</summary>
public sealed record StartRequest(WorkOrder Order = null, string SaveFile = null);

/// <summary>Carries the menu's choice across the scene change into the workshop.</summary>
public static class Session
{
    public const string MenuScene = "res://Scenes/UI/MainMenu.tscn";
    public const string WorkshopScene = "res://Scenes/Main.tscn";

    private static StartRequest _pending;

    /// <summary>Opens the workshop as asked; false when the save cannot be read.</summary>
    public static bool Start(SceneTree tree, StartRequest request)
    {
        if (request.SaveFile != null && !Save.SaveGame.Prepare(request.SaveFile))
            return false;
        _pending = request;
        tree.ChangeSceneToFile(WorkshopScene);
        return true;
    }

    /// <summary>The request the workshop starts with (once); null = free workshop.</summary>
    public static StartRequest Take()
    {
        var request = _pending;
        _pending = null;
        return request;
    }
}
