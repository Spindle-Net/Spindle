namespace Spindle.Abstractions.Snapshot;

public interface ISpindleSerializer
{
    SerializedPayload Serialize<T>(
        T value);

    T Deserialize<T>(
        SerializedPayload payload);

    object? Deserialize(
        SerializedPayload payload,
        Type type);
}