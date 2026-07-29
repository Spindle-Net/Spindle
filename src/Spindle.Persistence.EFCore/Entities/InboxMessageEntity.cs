using Spindle.Abstractions.Snapshot;
using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

/// <summary>
/// The entity framework representation of <see cref="Spindle.Persistence.Messaging.InboxMessageRecord"/>
/// </summary>
internal class InboxMessageEntity
{
    [Key]
    public required string MessageId { get; set; }

    public required string Kind { get; set; }

    public required SerializedPayload Payload { get; set; }

    public required DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

}
