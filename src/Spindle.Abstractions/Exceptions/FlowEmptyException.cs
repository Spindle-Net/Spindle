namespace Spindle.Abstractions.Exceptions;

public sealed class FlowEmptyException : SpindleException
{
    public FlowEmptyException()
        : base("The flow must contain at least one step.")
    {
    }
}