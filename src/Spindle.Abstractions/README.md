# Spindle.Abstractions

This is the abstractions package for `Spindle.Net`. It contains the core interfaces and types that define the contract for the Spindle framework. This package is intended to be used by developers who want to implement their own components or extend the functionality of Spindle.Net.

It can also be used to only reference the abstractions without needing to include the entire Spindle.Net package, which can be useful for reducing dependencies in certain scenarios.

## Workflow nodes

`Node` is the fundamental DAG dependency. Result-producing nodes derive from
`Node<TResult>` and can be awaited or supplied as typed inputs to later steps.

- `StepNode<TResult>` represents executable application work and supports step
  policies such as queues, retries, timeouts, and heartbeats.
- `DelayNode` represents a durable timer.
- `SignalNode<TSignal>` represents a durable signal wait and exposes its signal
  name and correlation key.
- `ConditionNode` represents a durable polling wait. It checks immediately once
  its inputs are ready, then schedules another check after each false result.
- `WaitAllNode` and `WaitAnyNode` are durable barriers whose results identify
  the terminal input outcomes or winning input node.

Waits are declared synchronously, so a flow can build parallel branches before
awaiting one of them:

```csharp
var acknowledgement = ctx.WaitForSignal(
    "team-ack",
    "Wait for team acknowledgement",
    new SignalName("ack"),
    new CorrelationKey("team-a"));
var timeout = ctx.Delay("timeout", "Escalation delay", TimeSpan.FromHours(1));
var first = ctx.WaitAny("first", acknowledgement, timeout);

var decision = ctx.Step<WaitAnyResult, bool>(
    "should-escalate",
    "Decide whether to escalate",
    first,
    result => ValueTask.FromResult(result.Winner.NodeId == timeout.Id));
```

Barriers default to terminal-outcome semantics. Pass
`BarrierCompletionMode.SuccessfulOnly` when only successful input nodes should
satisfy the barrier. `WaitAny` does not cancel inputs that did not win.
When no display name is supplied, a node uses its explicit ID as its persisted
name; explicit names remain available for operator-facing descriptions.

## Waiting for a condition

Use `WaitForCondition` when an external system cannot push a signal. Condition
callbacks run locally and may consume zero, one, or two typed node inputs. A
false result is not a failure: the node persists a timer and is checked again
after the polling interval.

```csharp
var delivery = ctx.Step(
    "delivery",
    "Create delivery",
    async () => await deliveryService.CreateAsync());

await ctx.WaitForCondition(
        "wait-for-delivery",
        "Wait for delivery",
        TimeSpan.FromMinutes(5),
        delivery,
        details => deliveryService.CheckIsDeliveredAsync(details.Id))
    .WithTimeout(TimeSpan.FromDays(31));
```

Timeouts are measured from the node's original declaration and survive flow
replay. A timed-out condition throws `TimeoutException` when awaited. Exceptions
from the condition callback fail the node immediately; only a returned `false`
schedules another poll.
