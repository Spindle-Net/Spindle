using Microsoft.EntityFrameworkCore;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.Messaging;
using System.Linq.Expressions;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreOutboxStore(SpindleDbContext context) : IOutboxStore
{

    public async ValueTask AddAsync(
        OutboxMessageRecord message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await context.OutboxMessages.AddAsync(new OutboxMessageEntity
        {
            MessageId = message.MessageId,
            Kind = message.Kind,
            Payload = message.Payload,
            Headers = message.Headers,
            CreatedAt = message.CreatedAt,
            PublishedAt = message.PublishedAt,
        }, cancellationToken);
    }

    private readonly static Expression<Func<OutboxMessageEntity, OutboxMessageRecord>> Transformer = x => new OutboxMessageRecord
    {
        MessageId = x.MessageId,
        Kind = x.Kind,
        CreatedAt = x.CreatedAt,
        PublishedAt = x.PublishedAt,
        Payload = x.Payload,
        Headers = x.Headers
    };

    public async ValueTask<IReadOnlyList<OutboxMessageRecord>> GetUnpublishedAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PublishedAt != null)
            .OrderBy(message => message.CreatedAt)
            .Take(maxCount)
            .Select(Transformer)
            .ToArrayAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask MarkPublishedAsync(
        string messageId,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await context.OutboxMessages
            .Where(x => x.MessageId == messageId)
            .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.PublishedAt, _ => publishedAt),
                    cancellationToken: cancellationToken);
    }
}
