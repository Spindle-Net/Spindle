using Spindle.Persistence.Timers;

namespace Spindle;

internal sealed record TimerNodeInitialization(TimerRecord Timer) : NodeInitialization;
