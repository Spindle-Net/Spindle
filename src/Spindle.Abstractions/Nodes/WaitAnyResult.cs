namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Identifies the input selected by a wait-any barrier.
/// </summary>
public sealed record WaitAnyResult(NodeOutcome Winner);
