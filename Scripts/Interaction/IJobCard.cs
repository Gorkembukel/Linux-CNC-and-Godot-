namespace CncTrainer.Interaction;

/// <summary>The job the player is working on, shown on the HUD wherever
/// they are (the setup sheets only show near their machine). Implementers
/// join the "job_cards" group.</summary>
public interface IJobCard
{
    const string Group = "job_cards";

    /// <summary>Empty when there is no job.</summary>
    string CardText { get; }
}
