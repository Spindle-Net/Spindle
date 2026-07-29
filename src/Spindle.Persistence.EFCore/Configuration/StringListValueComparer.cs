using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Spindle.Persistence.EFCore.Configuration;

internal sealed class StringListValueComparer()
    : ValueComparer<List<string>>(
        (left, right) => ValuesEqual(left, right),
        values => GetValueHashCode(values),
        values => values.ToList())
{
    private static bool ValuesEqual(
        List<string>? left,
        List<string>? right)
    {
        return left != null && right != null && left.SequenceEqual(right);
    }

    private static int GetValueHashCode(List<string> values)
    {
        return values.Aggregate(0, HashCode.Combine);
    }
}
