using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Describes the terminal outcome observed for a node.
/// </summary>
public sealed record NodeOutcome(NodeId NodeId, NodeStatus Status);
