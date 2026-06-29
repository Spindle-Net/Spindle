using Spindle.Abstractions.Core;

namespace Spindle.Transport;

public sealed record SpindleSubscription
{
    public required ApplicationName Application { get; init; }

    public QueueName? Queue { get; init; }

    public IReadOnlySet<SpindleMessageKind> MessageKinds { get; init; } =
        new HashSet<SpindleMessageKind>();
}
