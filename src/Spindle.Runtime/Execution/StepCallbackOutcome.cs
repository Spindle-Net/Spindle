namespace Spindle;

internal enum StepCallbackOutcomeKind
{
    /// <summary>
    /// The callback returned a result before any deadline.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The callback threw an exception before any deadline.
    /// </summary>
    Faulted,

    /// <summary>
    /// The attempt reached its timeout. Anything the callback produced afterwards is discarded.
    /// </summary>
    TimedOut,

    /// <summary>
    /// The caller or host cancelled execution before the attempt finished.
    /// </summary>
    Interrupted
}

/// <summary>
/// How a single invocation of a step callback ended.
/// </summary>
internal readonly record struct StepCallbackOutcome(
    StepCallbackOutcomeKind Kind,
    object? Result = null,
    Exception? Exception = null)
{
    public static StepCallbackOutcome Succeeded(object? result) => new(StepCallbackOutcomeKind.Succeeded, Result: result);

    public static StepCallbackOutcome Faulted(Exception exception) => new(StepCallbackOutcomeKind.Faulted, Exception: exception);

    public static StepCallbackOutcome TimedOut { get; } = new(StepCallbackOutcomeKind.TimedOut);

    public static StepCallbackOutcome Interrupted { get; } = new(StepCallbackOutcomeKind.Interrupted);
}
