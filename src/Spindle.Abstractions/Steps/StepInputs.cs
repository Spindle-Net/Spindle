namespace Spindle.Abstractions.Steps;

public readonly struct StepInputs(IReadOnlyList<object?> values)
{
    public int Count => values.Count;

    public T Get<T>(int index)
    {
        if ((uint)index >= (uint)values.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var value = values[index];

        if (value is T typed)
        {
            return typed;
        }

        if (value is null && default(T) is null)
        {
            return default!;
        }

        throw new InvalidCastException(
            $"Step input at index {index} is of type '{value?.GetType().FullName ?? "<null>"}', " +
            $"but '{typeof(T).FullName}' was requested.");
    }
}