namespace Spindle.Transport;

public enum SpindleMessageKind
{
    StepReady,

    ExecuteStep,
    StepCompleted,
    StepFailed,
    StepHeartbeat,

    TimerDue,
    SignalRaised,

    FlowStarted,
    FlowCompleted,
    FlowFailed,

    StartRemoteFlow,
    RemoteFlowStarted,
    RemoteFlowCompleted,
    RemoteFlowFailed,
    RemoteFlowCancelled
}
