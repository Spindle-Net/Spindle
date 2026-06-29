namespace Spindle.Persistence.Messaging;

public interface IOutboxStore
{
    ValueTask AddAsync(
        OutboxMessageRecord message,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<OutboxMessageRecord>> GetUnpublishedAsync(
        int maxCount,
        CancellationToken cancellationToken = default);

    ValueTask MarkPublishedAsync(
        string messageId,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken = default);
}
