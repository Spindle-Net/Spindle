using Spindle.Abstractions.Snapshot;

namespace Spindle;

internal static class SerializerReflection
{
    public static SerializedPayload Serialize(
        ISpindleSerializer serializer,
        object? value,
        Type type)
    {
        var method = typeof(ISpindleSerializer)
            .GetMethod(nameof(ISpindleSerializer.Serialize))!
            .MakeGenericMethod(type);

        return (SerializedPayload)method.Invoke(serializer, [value])!;
    }
}
