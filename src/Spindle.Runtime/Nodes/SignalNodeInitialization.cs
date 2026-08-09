using Spindle.Persistence.Signals;

namespace Spindle;

internal sealed record SignalNodeInitialization(SignalWaitRecord SignalWait) : NodeInitialization;
