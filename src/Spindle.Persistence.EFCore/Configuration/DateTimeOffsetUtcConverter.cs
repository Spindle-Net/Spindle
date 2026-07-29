using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Spindle.Persistence.EFCore.Configuration;

internal sealed class DateTimeOffsetUtcConverter()
    : ValueConverter<DateTimeOffset, DateTimeOffset>(
        value => value.ToUniversalTime(),
        value => value);
