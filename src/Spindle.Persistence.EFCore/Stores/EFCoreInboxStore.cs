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
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

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
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

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
