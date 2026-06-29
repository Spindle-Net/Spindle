using System.Text.Json;
using Spindle.Abstractions.Snapshot;

namespace Spindle;

/// <summary>
/// Serializer implementation that serializes objects as Json using <see cref="System.Text.Json"/>.
/// </summary>
public sealed class JsonSpindleSerializer : ISpindleSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public SerializedPayload Serialize<T>(T value)
    {
        return new SerializedPayload
        {
            ContentType = "application/json",
            TypeName = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name,
            Data = JsonSerializer.SerializeToUtf8Bytes(value, Options)
        };
    }

    public T Deserialize<T>(SerializedPayload payload)
    {
        return JsonSerializer.Deserialize<T>(payload.Data, Options)!;
    }

    public object? Deserialize(SerializedPayload payload, Type type)
    {
        return JsonSerializer.Deserialize(payload.Data, type, Options);
    }
}
