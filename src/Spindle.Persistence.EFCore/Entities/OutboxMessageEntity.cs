using Spindle.Abstractions.Snapshot;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Spindle.Persistence.EFCore.Entities;

[Index(nameof(PublishedAt), nameof(CreatedAt))]
internal class OutboxMessageEntity
{
    [Key]
    [MaxLength(255)]
    public required string MessageId { get; init; }

    public required string Kind { get; init; }

    public required SerializedPayload Payload { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}
