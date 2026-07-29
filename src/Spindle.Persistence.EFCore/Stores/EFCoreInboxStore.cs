using Microsoft.EntityFrameworkCore;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.Messaging;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreInboxStore(SpindleDbContext context) : IInboxStore
{

    public async ValueTask<bool> TryRecordAsync(
        InboxMessageRecord message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Check if we already have it stored
        if (await context.InboxMessages
                .AsNoTracking()
                .AnyAsync(x => x.MessageId == message.MessageId, 
                    cancellationToken: cancellationToken))
            return false;

        // It doesn't exist, so we add it
        await context.InboxMessages.AddAsync(new InboxMessageEntity
        {
            MessageId = message.MessageId,
            Kind = message.Kind,
            Payload = message.Payload,
            ReceivedAt = message.ReceivedAt,
            ProcessedAt = message.ProcessedAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async ValueTask<InboxMessageRecord?> GetAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await context.InboxMessages
            .AsNoTracking()
            .Where(x => x.MessageId == messageId)
            .Select(x => new InboxMessageRecord
            {
                MessageId = x.MessageId,
                Kind = x.Kind,
                Payload = x.Payload,
                ReceivedAt = x.ReceivedAt,
                ProcessedAt = x.ProcessedAt
            })
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);
    }
}
