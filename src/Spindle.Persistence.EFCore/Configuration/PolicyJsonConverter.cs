using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Spindle.Persistence.EFCore.Configuration;

/// <summary>
/// Stores a step policy record as JSON text. Enums are written by name so stored policies stay
/// readable and do not depend on enum ordinals.
/// </summary>
internal sealed class PolicyJsonConverter<TPolicy>()
    : ValueConverter<TPolicy, string>(
        policy => JsonSerializer.Serialize(policy, PolicyJsonOptions.Options),
        json => JsonSerializer.Deserialize<TPolicy>(json, PolicyJsonOptions.Options)!)
    where TPolicy : class;

internal static class PolicyJsonOptions
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
