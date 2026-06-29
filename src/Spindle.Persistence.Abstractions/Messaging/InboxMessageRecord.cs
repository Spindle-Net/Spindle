using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.Messaging;

public sealed record InboxMessageRecord
{
    public required string MessageId { get; init; }

    public required string Kind { get; init; }

    public required SerializedPayload Payload { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; init; }
}
