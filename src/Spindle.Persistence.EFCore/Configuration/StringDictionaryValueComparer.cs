using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Spindle.Persistence.EFCore.Configuration;

internal sealed class StringDictionaryValueComparer()
    : ValueComparer<IReadOnlyDictionary<string, string>>(
        (left, right) => ValuesEqual(left, right),
        values => GetValueHashCode(values),
        values => values == null 
                ? new Dictionary<string, string>() 
                : new Dictionary<string, string>(values))
{
    private static bool ValuesEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (left == null && right == null) return true;
        return left != null &&
            right != null &&
            left.Count == right.Count &&
            left.All(pair =>
                right.TryGetValue(pair.Key, out var value) &&
                value == pair.Value);
    }

    private static int GetValueHashCode(
        IReadOnlyDictionary<string, string> values)
    {
        return values
            .OrderBy(pair => pair.Key)
            .Aggregate(
                0,
                (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value));
    }
}
