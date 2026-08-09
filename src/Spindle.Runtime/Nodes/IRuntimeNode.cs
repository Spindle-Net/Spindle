using Spindle.Abstractions.Core;

namespace Spindle;

internal interface IRuntimeNode
{
    Type ResultType { get; }

    FlowInstanceId FlowInstanceId { get; }
}
