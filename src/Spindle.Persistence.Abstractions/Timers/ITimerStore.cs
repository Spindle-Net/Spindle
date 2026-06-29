using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Timers;

public interface ITimerStore
{
    ValueTask CreateAsync(
        TimerRecord timer,
        CancellationToken cancellationToken = default);

    ValueTask<TimerRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TimerRecord>> GetDueAsync(
        DateTimeOffset dueAtOrBefore,
        int maxCount,
        CancellationToken cancellationToken = default);

    ValueTask MarkFiredAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset firedAt,
        CancellationToken cancellationToken = default);
}
