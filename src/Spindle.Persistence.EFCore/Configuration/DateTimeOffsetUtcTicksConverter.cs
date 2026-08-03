using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Spindle.Persistence.EFCore.Configuration;

internal sealed class DateTimeOffsetUtcTicksConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero));
