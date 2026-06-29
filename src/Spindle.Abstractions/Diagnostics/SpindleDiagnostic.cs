using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Diagnostics;

public sealed record SpindleDiagnostic
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public required SpindleDiagnosticSeverity Severity { get; init; }

    public StepId? StepId { get; init; }

    public string? HelpLink { get; init; }
}