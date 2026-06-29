using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Steps;

namespace Spindle.Persistence.InMemory.Stores;

internal static class InMemoryRecordCopies
{
    public static SerializedPayload? Copy(SerializedPayload? payload)
    {
        return payload is null
            ? null
            : payload with { Data = payload.Data.ToArray() };
    }

    public static FlowDefinitionRecord Copy(FlowDefinitionRecord record)
    {
        return record with { Definition = Copy(record.Definition) };
    }

    public static FlowInstanceRecord Copy(FlowInstanceRecord record)
    {
        return record with
        {
            Input = Copy(record.Input)!,
            Result = Copy(record.Result)
        };
    }

    public static StepInstanceRecord Copy(StepInstanceRecord record)
    {
        return record with
        {
            Dependencies = record.Dependencies.ToArray(),
            Input = Copy(record.Input),
            Result = Copy(record.Result)
        };
    }

    public static SignalRecord Copy(SignalRecord record)
    {
        return record with { Payload = Copy(record.Payload)! };
    }

    public static OutboxMessageRecord Copy(OutboxMessageRecord record)
    {
        return record with
        {
            Payload = Copy(record.Payload)!,
            Headers = new Dictionary<string, string>(record.Headers)
        };
    }

    public static InboxMessageRecord Copy(InboxMessageRecord record)
    {
        return record with { Payload = Copy(record.Payload)! };
    }

    public static ExecutionHistoryRecord Copy(ExecutionHistoryRecord record)
    {
        return record with { Payload = Copy(record.Payload) };
    }
}
