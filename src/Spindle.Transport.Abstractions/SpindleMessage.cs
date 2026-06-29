using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Transport;

public sealed record SpindleMessage
{
    public required SpindleMessageId MessageId { get; init; }

    public required SpindleMessageKind Kind { get; init; }

    public required ApplicationName SourceApplication { get; init; }

    public ApplicationName? TargetApplication { get; init; }

    public QueueName? Queue { get; init; }

    public FlowInstanceId? FlowInstanceId { get; init; }

    public StepId? StepId { get; init; }

    public StepAttemptId? AttemptId { get; init; }

    public CorrelationKey? CorrelationKey { get; init; }

    public required SerializedPayload Payload { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
}
