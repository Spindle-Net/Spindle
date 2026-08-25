using Spindle.Persistence.Conditions;

namespace Spindle;

internal sealed record ConditionNodeInitialization(ConditionWaitRecord ConditionWait) : NodeInitialization;
