using Spindle.Abstractions.Diagnostics;

namespace Spindle.Abstractions.Exceptions;

public sealed class FlowValidationException : SpindleException
{
    public IReadOnlyList<SpindleDiagnostic> Diagnostics { get; }

    public FlowValidationException(
        IReadOnlyList<SpindleDiagnostic> diagnostics)
        : base("The flow definition is invalid.")
    {
        Diagnostics = diagnostics;
    }
}