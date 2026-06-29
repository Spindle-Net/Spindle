using Microsoft.Extensions.Logging;
using Spindle.Persistence.Steps;

namespace Spindle;

internal class StepLogger(FlowExecutionSession session, StepInstanceRecord running, StepInstanceRecord step, ILogger? externalLogger) : ILogger
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        externalLogger?.Log(logLevel, eventId, state, exception, (_, _) => $"[FlowInstanceId: {session.FlowInstanceId}] [StepId: {step.StepId}] [Attempt: {running.Attempt}] {message}");

        // TODO: Save these logs somewhere
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;
}