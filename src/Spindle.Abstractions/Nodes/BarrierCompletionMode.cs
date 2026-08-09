namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Controls which input outcomes allow a barrier node to complete successfully.
/// </summary>
public enum BarrierCompletionMode
{
    /// <summary>Complete when the required inputs reach any terminal state.</summary>
    Terminal,

    /// <summary>Complete only when the required inputs complete successfully.</summary>
    SuccessfulOnly
}
