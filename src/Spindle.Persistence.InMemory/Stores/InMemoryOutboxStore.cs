using Spindle.Persistence.Messaging;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OutboxMessageRecord> _messages = new(StringComparer.Ordinal);

    public ValueTask AddAsync(
        OutboxMessageRecord message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _messages.Add(message.MessageId, InMemoryRecordCopies.Copy(message));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<OutboxMessageRecord>> GetUnpublishedAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var messages = _messages.Values
                .Where(message => message.PublishedAt is null)
                .OrderBy(message => message.CreatedAt)
                .Take(maxCount)
                .Select(InMemoryRecordCopies.Copy)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<OutboxMessageRecord>>(messages);
        }
    }

    public ValueTask MarkPublishedAsync(
        string messageId,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_messages.TryGetValue(messageId, out var message))
            {
                _messages[messageId] = message with { PublishedAt = publishedAt };
            }
        }

        return ValueTask.CompletedTask;
    }
}
