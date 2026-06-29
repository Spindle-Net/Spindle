namespace Spindle;

internal static class AsyncBlocking
{
    public static T GetResult<T>(ValueTask<T> valueTask)
    {
        return valueTask.IsCompletedSuccessfully
            ? valueTask.Result
            : valueTask.AsTask().GetAwaiter().GetResult();
    }

    public static void GetResult(ValueTask valueTask)
    {
        if (valueTask.IsCompletedSuccessfully)
        {
            return;
        }

        valueTask.AsTask().GetAwaiter().GetResult();
    }
}
