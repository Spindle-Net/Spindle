using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Spindle.Persistence.EFCore.Configuration;

internal sealed class StringDictionaryJsonConverter()
    : ValueConverter<IReadOnlyDictionary<string, string>, string>(
        values => JsonSerializer.Serialize(values, JsonSerializerOptions.Default),
        json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Default)!);
