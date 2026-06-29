using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.Messaging;

public sealed record OutboxMessageRecord
{
    public required string MessageId { get; init; }

    public required string Kind { get; init; }

    public required SerializedPayload Payload { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}
