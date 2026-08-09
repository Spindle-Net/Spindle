using Spindle.Abstractions.Snapshot;

namespace Spindle;

internal sealed record BarrierEvaluation(SerializedPayload? Result, string? Error);
