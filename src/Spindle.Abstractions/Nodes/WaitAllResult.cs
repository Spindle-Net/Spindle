namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Contains the ordered outcomes observed by a wait-all barrier.
/// </summary>
public sealed record WaitAllResult(IReadOnlyList<NodeOutcome> Outcomes);
