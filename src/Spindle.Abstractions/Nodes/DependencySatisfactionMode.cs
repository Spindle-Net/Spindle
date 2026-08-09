namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Describes how a persisted node's dependencies are satisfied.
/// </summary>
public enum DependencySatisfactionMode
{
    AllSucceeded,
    AnySucceeded,
    AllTerminal,
    AnyTerminal
}
