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

## Retries

Give a step a `RetryPolicy` to run it again when its callback throws.
`MaxAttempts` counts the first attempt, so `RetryPolicy.Exponential(3)` runs
the step at most three times. Steps without a policy, or with
`RetryPolicy.None`, run once.

```csharp
var charge = await ctx.Step("charge", "Charge card", async () =>
    {
        return await payments.ChargeAsync(order);
    })
    .WithRetry(RetryPolicy.Exponential(3));
```

Policies can also be passed as `StepOptions` when the step is declared. A
step's retry and timeout policies are stored when it is first persisted, and
later replays keep using those stored policies even if the flow code changes.

After failed attempt `n`, the next attempt waits `InitialDelay` (fixed),
`InitialDelay × n` (linear) or `InitialDelay × 2^(n−1)` (exponential),
multiplied by a random factor in `1 ± JitterFactor` and capped at `MaxDelay`.
While it waits, the step is `Waiting` with a `RetryAt` time and a durable
timer, so the retry survives restarts. Dependent steps and wait barriers
treat a waiting step as unfinished, and completed steps are not run again.

Only exceptions thrown by the step callback are retried, including a
`TimeoutException` thrown by the callback itself. Failures in Spindle itself,
such as a result that cannot be serialized, fail the step immediately.

## Timeouts

`WithTimeout` limits how long one attempt may run:

```csharp
var rates = await ctx.Step("rates", "Fetch rates", async stepContext =>
    {
        return await rateClient.GetAsync(stepContext.CancellationToken);
    })
    .WithTimeout(new TimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30),
        CancellationGracePeriod = TimeSpan.FromSeconds(5),
        Behavior = TimeoutBehavior.Fail
    })
    .WithRetry(RetryPolicy.Exponential(3));
```

When the timeout elapses, `IStepExecutionContext.CancellationToken` is
cancelled and the callback has `CancellationGracePeriod` (five seconds by
default) to stop. The attempt then counts as timed out, even if the callback
returned a value during cleanup, and `Behavior` decides what happens:

- `Fail` retries the step if its retry policy allows it, and otherwise ends it
  as `TimedOut`.
- `Cancel` marks the step `TimedOut` and cancels the flow, without retrying.
- `Continue` completes the step with `default(TResult)` so the flow carries on,
  without retrying.

Spindle cannot stop code that ignores the cancellation token. Such a callback
is abandoned and keeps running in the process: it may still cause side
effects, and it can overlap a retry of the same step. Its result is discarded.

Cancelling the token passed to `RunAsync`, or stopping the host, interrupts
running steps without failing them. The interrupted attempt is recorded as
cancelled, does not use up the retry budget, and the step runs again the next
time the flow is advanced. `CancelAsync` still cancels the whole flow.

## Retrying failed steps manually

`ISpindleRuntime.RetryAsync` schedules failed or timed-out steps to run again,
for example after fixing a downstream outage:

```csharp
// Retry one step.
await runtime.RetryAsync(instanceId, new NodeId("charge"));

// Retry every failed or timed-out step in the flow.
await runtime.RetryAsync(instanceId);
```

Each retried step gets a fresh retry budget. Completed steps and the full
attempt history are kept, and failed wait barriers that depend on a retried
step are evaluated again. `RetryAsync` works on active, failed and timed-out
flows; it rejects completed and cancelled flows, unknown steps, and steps that
are not failed or timed out. It only schedules the steps: they run on the next
`RunAsync` call or worker pass.
