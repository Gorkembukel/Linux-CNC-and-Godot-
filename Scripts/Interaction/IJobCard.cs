namespace CncTrainer.Interaction;

/// <summary>The job the player is working on, shown on the HUD wherever
/// they are (the setup sheets only show near their machine). Implementers
/// join the "job_cards" group.</summary>
public interface IJobCard
{
    const string Group = "job_cards";

    /// <summary>Empty when there is no job.</summary>
    string CardText { get; }

    /// <summary>The job's technical drawing, null when it has none.</summary>
    JobDrawing CardDrawing => null;
}

/// <summary>A technical drawing: the sheet (lines, as imported from SVG) and
/// the text written over it, in the sheet's own pixels.</summary>
public sealed record JobDrawing(Godot.Texture2D Sheet,
    System.Collections.Generic.IReadOnlyList<(Godot.Vector2 At, string Text, int SizePx)> Notes);
