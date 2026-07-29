using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Spindle.Persistence.EFCore.Configuration;

internal sealed class StringListJsonConverter()
    : ValueConverter<List<string>, string>(
        values => JsonSerializer.Serialize(values, JsonSerializerOptions.Default),
        json => JsonSerializer.Deserialize<List<string>>(json, JsonSerializerOptions.Default)!);
