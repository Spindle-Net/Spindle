namespace Spindle;

internal sealed record StepCallbackOutcome(object? Result, Exception? Exception, bool TimedOut);
