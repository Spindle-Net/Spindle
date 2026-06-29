namespace Spindle.Abstractions.Snapshot;

public sealed record SerializedPayload
{
    public required string ContentType { get; init; }

    public required string TypeName { get; init; }

    public required byte[] Data { get; init; }
}