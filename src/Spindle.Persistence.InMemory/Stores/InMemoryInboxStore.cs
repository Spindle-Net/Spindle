using Spindle.Persistence.Messaging;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryInboxStore : IInboxStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, InboxMessageRecord> _messages = new(StringComparer.Ordinal);

    public ValueTask<bool> TryRecordAsync(
        InboxMessageRecord message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_messages.ContainsKey(message.MessageId))
            {
                return ValueTask.FromResult(false);
            }

            _messages.Add(message.MessageId, InMemoryRecordCopies.Copy(message));
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<InboxMessageRecord?> GetAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _messages.TryGetValue(messageId, out var message)
                    ? InMemoryRecordCopies.Copy(message)
                    : null);
        }
    }
}
