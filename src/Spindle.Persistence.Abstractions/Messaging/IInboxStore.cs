namespace Spindle.Persistence.Messaging;

public interface IInboxStore
{
    ValueTask<bool> TryRecordAsync(
        InboxMessageRecord message,
        CancellationToken cancellationToken = default);

    ValueTask<InboxMessageRecord?> GetAsync(
        string messageId,
        CancellationToken cancellationToken = default);
}
